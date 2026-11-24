using System;
using System.Collections.Generic;
using System.Globalization;

namespace Xiaohei
{
    internal static class HolidayCalendar
    {
        private static readonly ChineseLunisolarCalendar Lunar = new ChineseLunisolarCalendar();
        private static readonly string[] LunarMonths =
            { "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月" };
        private static readonly string[] LunarDays = {
            "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
            "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
            "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
        };

        internal static string LunarLabel(DateTime date)
        {
            date = date.Date;
            if (date < Lunar.MinSupportedDateTime || date > Lunar.MaxSupportedDateTime) return null;
            int day = Lunar.GetDayOfMonth(date);
            if (day != 1) return LunarDays[day - 1];
            int month = Lunar.GetMonth(date);
            int leap = Lunar.GetLeapMonth(Lunar.GetYear(date));
            // .NET counts the leap month as an extra month in sequence.
            int ordinaryMonth = leap > 0 && month >= leap ? month - 1 : month;
            return (month == leap ? "闰" : "") + LunarMonths[ordinaryMonth - 1];
        }

        // 国务院办公厅公布的放假日期，起止日均计入假期。
        private static readonly int[,] HolidayRanges =
        {
            // 2025 年，国办发明电〔2024〕12号
            { 20250101, 20250101 }, { 20250128, 20250204 }, { 20250404, 20250406 },
            { 20250501, 20250505 }, { 20250531, 20250602 }, { 20251001, 20251008 },

            // 2026 年，国办发明电〔2025〕7号
            { 20260101, 20260103 }, { 20260215, 20260223 }, { 20260404, 20260406 },
            { 20260501, 20260505 }, { 20260619, 20260621 }, { 20260925, 20260927 },
            { 20261001, 20261007 },

            // 2027 年，国办发明电〔2026〕10号
            { 20270101, 20270103 }, { 20270205, 20270213 }, { 20270403, 20270405 },
            { 20270501, 20270505 }, { 20270609, 20270609 }, { 20270915, 20270915 },
            { 20271001, 20271007 }
        };

        // 节日名称只标在当天。清明及农历节日按香港天文台公历与农历对照表核对。
        private static readonly Dictionary<int, string> FestivalNames = new Dictionary<int, string>
        {
            { 20250101, "元旦" }, { 20250128, "除夕" }, { 20250129, "春节" }, { 20250404, "清明节" },
            { 20250501, "劳动节" }, { 20250531, "端午节" }, { 20251001, "国庆节" }, { 20251006, "中秋节" },
            { 20260101, "元旦" }, { 20260216, "除夕" }, { 20260217, "春节" }, { 20260405, "清明节" },
            { 20260501, "劳动节" }, { 20260619, "端午节" }, { 20260925, "中秋节" }, { 20261001, "国庆节" },
            { 20270101, "元旦" }, { 20270205, "除夕" }, { 20270206, "春节" }, { 20270405, "清明节" },
            { 20270501, "劳动节" }, { 20270609, "端午节" }, { 20270915, "中秋节" }, { 20271001, "国庆节" }
        };

        // 同上三份国务院办公厅通知中注明要上班的周末。
        private static readonly HashSet<int> MakeupWorkdays = new HashSet<int>
        {
            20250126, 20250208, 20250427, 20250928, 20251011,
            20260104, 20260214, 20260228, 20260509, 20260920, 20261010,
            20270131, 20270214, 20270508, 20270926, 20271009
        };

        private static int DateKey(DateTime date) { return date.Year * 10000 + date.Month * 100 + date.Day; }

        internal static string FestivalName(DateTime date)
        {
            string name;
            return FestivalNames.TryGetValue(DateKey(date), out name) ? name : null;
        }

        internal static bool IsMakeupWorkday(DateTime date) { return MakeupWorkdays.Contains(DateKey(date)); }

        internal static bool IsHoliday(DateTime date)
        {
            int day = DateKey(date);
            for (int i = 0; i < HolidayRanges.GetLength(0); i++)
            {
                if (day < HolidayRanges[i, 0]) return false;
                if (day <= HolidayRanges[i, 1]) return true;
            }
            return false;
        }
    }
}
