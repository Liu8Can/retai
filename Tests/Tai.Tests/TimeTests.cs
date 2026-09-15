using Core.Librarys;
using System;
using Xunit;

namespace Tai.Tests
{
    /// <summary>
    /// Tests for Core.Librarys.Time utility methods.
    /// These are pure-function tests with no external dependencies.
    /// </summary>
    public class TimeTests
    {
        [Theory]
        [InlineData(30, "30秒")]
        [InlineData(59, "59秒")]
        [InlineData(60, "1分钟")]
        [InlineData(90, "1分钟30秒")]
        [InlineData(3600, "1小时")]
        [InlineData(3660, "1小时1分")]
        [InlineData(7200, "2小时")]
        public void ToString_FormatsCorrectly(int seconds, string expected)
        {
            var result = Time.ToString(seconds);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ToString_ZeroSeconds_ReturnsZero()
        {
            var result = Time.ToString(0);
            Assert.Equal("0秒", result);
        }

        [Theory]
        [InlineData(3600, "1.00")]  // exactly 1 hour => >0.1 => "1.00"
        [InlineData(1800, "0.50")]  // 0.5 hour => "0.50"
        [InlineData(36, "0.01")]    // 0.01 hour => >0.1 is false => "0"
        [InlineData(360, "0.10")]   // 0.1 hour => >0.1 is false => "0"
        public void ToHoursString_ConvertsSecondsToHoursString(double seconds, string expected)
        {
            var result = Time.ToHoursString(seconds);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ToHoursString_LessThan01Hour_ReturnsZero()
        {
            var result = Time.ToHoursString(359); // 0.0997... hours
            Assert.Equal("0", result);
        }

        [Fact]
        public void GetMonthDate_NormalMonth_ReturnsFirstAndLastDay()
        {
            var date = new DateTime(2025, 3, 15);
            var range = Time.GetMonthDate(date);

            Assert.Equal(new DateTime(2025, 3, 1), range[0]);
            Assert.Equal(new DateTime(2025, 3, 31), range[1]);
        }

        [Fact]
        public void GetMonthDate_FebruaryNonLeapYear_Returns28Days()
        {
            var date = new DateTime(2025, 2, 15);
            var range = Time.GetMonthDate(date);

            Assert.Equal(new DateTime(2025, 2, 1), range[0]);
            Assert.Equal(new DateTime(2025, 2, 28), range[1]);
        }

        [Fact]
        public void GetMonthDate_FebruaryLeapYear_Returns29Days()
        {
            var date = new DateTime(2024, 2, 15);
            var range = Time.GetMonthDate(date);

            Assert.Equal(new DateTime(2024, 2, 1), range[0]);
            Assert.Equal(new DateTime(2024, 2, 29), range[1]);
        }

        [Fact]
        public void GetYearDate_ReturnsJan1ToDec31()
        {
            var date = new DateTime(2025, 6, 15);
            var range = Time.GetYearDate(date);

            Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0), range[0]);
            Assert.Equal(new DateTime(2025, 12, 31, 23, 59, 59), range[1]);
        }
    }
}
