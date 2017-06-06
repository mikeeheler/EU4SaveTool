namespace EU4SaveTool
{
    /// <summary>
    /// Emulates a 'date' in EU4, which comes in two forms: a yyyy.mm.dd tuple and an integer representing the number
    /// of hours since Midnight, 1 Jan 5000 BCE. The former is used in text serialization and the latter is used in
    /// binary serialization.
    /// </summary>
    public sealed class EU4Date
    {
        public static readonly EU4Date StartingDate = new EU4Date(1444, 11, 11);

        private static readonly int[] _monthOffsets =
        {
            0,    31,  59,  90, 120, 151,
            181, 212, 243, 273, 304, 334
        };

        private const int _yearOffset = -5000;
        private const int _hoursPerDay = 24;
        private const int _daysPerYear = 365;
        private const int _hoursPerYear = _hoursPerDay * _daysPerYear;

        public EU4Date()
            : this(_yearOffset, 1, 1)
        {
        }

        public EU4Date(int year, int month, int day)
        {
            Year = year;
            Month = month;
            Day = day;
        }

        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public int Hour { get; set; }

        public static EU4Date FromInt(int value)
        {
            int year = value / _hoursPerYear + _yearOffset;
            int remainder = value % _hoursPerYear;
            int day = (remainder / _hoursPerDay) + 1;
            int month = GetMonthFromDay(day);

            return new EU4Date
            {
                Year = year,
                Month = month,
                Day = day - _monthOffsets[month-1],
                Hour = value % _hoursPerDay
            };
        }

        public int ToInt()
        {
            return ((Year - _yearOffset) + _monthOffsets[Month-1] + Day) * _hoursPerDay;
        }

        public override string ToString()
        {
            return $"{Year}.{Month}.{Day}";
        }

        private static int GetMonthFromDay(int day)
        {
            day = ((day - 1) % _daysPerYear) + 1;

            for (int i = 0; i < _monthOffsets.Length; ++i)
            {
                if (day <= _monthOffsets[i])
                {
                    return i;
                }
            }

            return _monthOffsets.Length;
        }
    }
}
