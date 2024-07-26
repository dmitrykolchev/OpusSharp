// <copyright file="TickCalibration.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Common;

/// <summary>
/// Provides functionality for synchronizing and calibrating tick values to the system clock.
/// </summary>
internal class TickCalibration
{
    // The performance counter to 100 ns tick conversion factor
    private static readonly double _qpcToHns;

    private readonly object _syncRoot = new();

    // The maximum performance counter ticks a QPC sync should take, otherwise it is rejected
    private readonly long _qpcSyncPrecision;

    // The maximum amount the QPC clock is allowed to drift since the last calibration
    private readonly long _maxClockDrift;

    // The high-water marks for elapsed ticks and system file time
    private long _tickHighWaterMark;
    private long _fileTimeHighWaterMark;

    // The calibration data array, which will be treated as a circular buffer containing the
    // latest calibration points, up to the specified capacity.
    private readonly CalibrationData[] _calibrationData;

    // Head and tail indices for the calibration data array
    private int _headIndex;
    private int _tailIndex;

    // The current number of calibration points
    private int _calibrationCount;

    /// <summary>
    /// Initializes static members of the <see cref="TickCalibration"/> class.
    /// </summary>
    static TickCalibration()
    {
        long qpcFrequency = Platform.Specific.TimeFrequency();
        _qpcToHns = 10000000.0 / qpcFrequency;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TickCalibration"/> class.
    /// </summary>
    /// <param name="capacity">The capacity of the calibration data array.</param>
    /// <param name="tickSyncPrecision">The maximum number of 100 ns ticks allowed for a sync operation.</param>
    /// <param name="maxClockDrift">The maximum allowable clock drift which, if exceeded, will update the calibration.</param>
    internal TickCalibration(
        int capacity = 512,
        long tickSyncPrecision = 10,
        long maxClockDrift = 10000)
    {
        _calibrationData = new CalibrationData[capacity];
        _calibrationCount = 0;
        _headIndex = 0;
        _tailIndex = 0;

        // Convert the sync precision in 100 ns ticks to QPC ticks
        _qpcSyncPrecision = (long)(tickSyncPrecision / _qpcToHns);
        _maxClockDrift = maxClockDrift;

        // Force an initial calibration
        Recalibrate(true);
    }

    /// <summary>
    /// Returns the system file time corresponding to the number of 100ns ticks from system boot.
    /// </summary>
    /// <param name="ticks">The number of 100ns ticks since system boot.</param>
    /// <param name="recalibrate">Recalibrates if necessary before conversion.</param>
    /// <returns>The system file time.</returns>
    public long ConvertToFileTime(long ticks, bool recalibrate = true)
    {
        if (recalibrate)
        {
            // Recalibrate only if clocks have drifted by more than the threshold since the last calibration
            Recalibrate();
        }

        lock (_syncRoot)
        {
            // Find the calibration entry to use for the conversion. Start with the most recent and walk
            // backwards, since we will normally be converting recent tick values going forward in time.
            // Assumes at least one entry, which will be the case due to the initial calibration on construction.
            int calIndex = (_headIndex + _calibrationCount - 1) % _calibrationData.Length;

            // Save the index of the next calibration point (if any) as we may need it for the conversion
            int nextCalIndex = _tailIndex;

            // Walk the circular buffer beginning with the most recent calibration point until we find
            // the one that applies to the ticks which we are trying to convert.
            while (calIndex != _headIndex && ticks < _calibrationData[calIndex].Ticks)
            {
                nextCalIndex = calIndex;

                // This just decrements the index in the circular buffer
                if (--calIndex < 0)
                {
                    calIndex = _calibrationData.Length - 1;
                }
            }

            // Do the conversion using the calibration point we just found.
            long fileTime = _calibrationData[calIndex].FileTime + (ticks - _calibrationData[calIndex].Ticks);

            // Clamp the result to the system file time of the following calibration point (if any).
            // This ensures monotonicity of the converted system file times.
            if (nextCalIndex != _tailIndex && fileTime > _calibrationData[nextCalIndex].FileTime)
            {
                fileTime = _calibrationData[nextCalIndex].FileTime;
            }

            // Bump up the high-water marks to guarantee stability of the current converted value,
            // irrespective of any future calibration adjustment.
            if (ticks > _tickHighWaterMark)
            {
                _tickHighWaterMark = ticks;
                _fileTimeHighWaterMark = fileTime;
            }

            return fileTime;
        }
    }

    /// <summary>
    /// Attempts to recalibrate elapsed ticks against the system time. The current elapsed ticks from
    /// the performance counter will be compared against the current system time and the calibration
    /// data will be modified only if it is determined that the times have drifted by more than the
    /// maximum allowed amount since the last calibration.
    /// </summary>
    /// <param name="force">Forces the calibration data to be modified regardless of the observed drift.</param>
    internal void Recalibrate(bool force = false)
    {
        long ft, qpc, qpc0;
        do
        {
            // Sync QPC and system time to within the specified precision. In order for the
            // sync to be precise, both calls to get system time and QPC should ideally occur
            // at exactly the same instant, as one atomic operation, to prevent a possible
            // thread context switch which would throw the calibration off. Since that is
            // not possible, we measure the time it took to sync and if that exceeds a maximum,
            // we repeat the process until both calls complete within the time limit.
            qpc0 = Platform.Specific.TimeStamp();
            ft = Platform.Specific.SystemTime();
            qpc = Platform.Specific.TimeStamp();
        }
        while ((qpc - qpc0) > _qpcSyncPrecision);

        // Convert raw QPC value to 100 ns ticks
        long ticks = (long)(_qpcToHns * qpc);

        lock (_syncRoot)
        {
            // Only recalibrate above the high-water mark
            if (ticks > _tickHighWaterMark)
            {
                // Calculate the current time using the most recent calibration data
                long fileTimeCal = 0;
                if (_calibrationCount > 0)
                {
                    int last = (_headIndex + _calibrationCount - 1) % _calibrationData.Length;
                    fileTimeCal = _calibrationData[last].FileTime + (ticks - _calibrationData[last].Ticks);
                }

                // Drift is the difference between the observed and calculated current file time
                long fileTimeDrift = ft - fileTimeCal;
                long fileTimeDriftAbs = fileTimeDrift < 0 ? -fileTimeDrift : fileTimeDrift;

                // Add the new calibration data if force is true or max drift exceeded
                if (force || fileTimeDriftAbs > _maxClockDrift)
                {
                    AddCalibrationData(ticks, ft);
                }
            }
        }
    }

    /// <summary>
    /// Adds a new calibration data point, adjusted accordingly in order to guarantee stability and monotonicity.
    /// </summary>
    /// <param name="ticks">The elapsed ticks.</param>
    /// <param name="fileTime">The corresponding system file time.</param>
    internal void AddCalibrationData(long ticks, long fileTime)
    {
        // Check that the new calibration data does not overlap with existing calibration data
        int last = (_headIndex + _calibrationCount - 1) % _calibrationData.Length;
        if (_calibrationCount == 0 || ticks > _calibrationData[last].Ticks)
        {
            // Once a high-water mark has been established for system file times on the existing calibration
            // data, ensure that the new calibration data does not affect results below this point to preserve
            // stability and monotonicity by shifting the new calibration point forward in time to the point
            // on the line that corresponds to the high-water mark if necessary.
            if (fileTime < _fileTimeHighWaterMark)
            {
                ticks = ticks + (_fileTimeHighWaterMark - fileTime);
                fileTime = _fileTimeHighWaterMark;
            }

            // Insert the new calibration data in the circular buffer. The oldest entry will
            // first be removed if the buffer is full.
            EnsureCapacity();
            _calibrationData[_tailIndex] = new CalibrationData(ticks, fileTime);
            _tailIndex = (_tailIndex + 1) % _calibrationData.Length;
            _calibrationCount++;
        }
    }

    /// <summary>
    /// Ensures that the calibration buffer has enough space for at least one new entry.
    /// If not, the oldest entry is removed.
    /// </summary>
    private void EnsureCapacity()
    {
        // Check if existing array is full
        if (_calibrationCount == _calibrationData.Length)
        {
            // Remove the head (oldest) calibration data
            _headIndex = (_headIndex + 1) % _calibrationData.Length;
            _calibrationCount--;
        }
    }

    /// <summary>
    /// Defines a single calibration point between elapsed ticks and the system file time.
    /// </summary>
    internal struct CalibrationData
    {
        private readonly long _ticks;
        private readonly long _fileTime;

        /// <summary>
        /// Initializes a new instance of the <see cref="CalibrationData"/> struct.
        /// </summary>
        /// <param name="ticks">The elapsed ticks.</param>
        /// <param name="fileTime">The system file time.</param>
        public CalibrationData(long ticks, long fileTime)
        {
            _ticks = ticks;
            _fileTime = fileTime;
        }

        /// <summary>
        /// Gets the calibration tick value.
        /// </summary>
        public readonly long Ticks => _ticks;

        /// <summary>
        /// Gets the calibration system file time.
        /// </summary>
        public readonly long FileTime => _fileTime;
    }
}
