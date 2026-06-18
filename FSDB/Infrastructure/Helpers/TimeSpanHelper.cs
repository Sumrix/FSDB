using System;

namespace FSDB.Infrastructure.Helpers;

public static class TimeSpanHelper
{
    internal static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min
        : value > max ? max
        : value;
}