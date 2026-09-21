using System;
using System.Globalization;
using Backgammon.Conversation;
using NUnit.Framework;
using UnityEngine;

namespace Nekolpos.TimeSystem.Editor
{
    public sealed class WeatherSystemTests
    {
        private const string GameStartDateTicksPrefsKey = "Nekolpos.Time.GameStartDateTicks";

        [Test]
        public void GenerateRandomWeather_MatchesWeightedBoundaries()
        {
            Assert.That(WeatherSystem.GenerateRandomWeather(0f), Is.EqualTo(WeatherType.Sunny));
            Assert.That(WeatherSystem.GenerateRandomWeather(0.6999f), Is.EqualTo(WeatherType.Sunny));
            Assert.That(WeatherSystem.GenerateRandomWeather(0.7f), Is.EqualTo(WeatherType.Cloudy));
            Assert.That(WeatherSystem.GenerateRandomWeather(0.8999f), Is.EqualTo(WeatherType.Cloudy));
            Assert.That(WeatherSystem.GenerateRandomWeather(0.9f), Is.EqualTo(WeatherType.Rainy));
            Assert.That(WeatherSystem.GenerateRandomWeather(0.9999f), Is.EqualTo(WeatherType.Rainy));
        }

        [Test]
        public void GenerateRandomWeather_IsApproximatelySeventyTwentyTen()
        {
            const int iterations = 100000;
            var random = new global::System.Random(12345);
            var sunny = 0;
            var cloudy = 0;
            var rainy = 0;

            for (var i = 0; i < iterations; i++)
            {
                switch (WeatherSystem.GenerateRandomWeather((float)random.NextDouble()))
                {
                    case WeatherType.Sunny:
                        sunny++;
                        break;
                    case WeatherType.Cloudy:
                        cloudy++;
                        break;
                    case WeatherType.Rainy:
                        rainy++;
                        break;
                }
            }

            Assert.That((float)sunny / iterations, Is.InRange(0.68f, 0.72f));
            Assert.That((float)cloudy / iterations, Is.InRange(0.18f, 0.22f));
            Assert.That((float)rainy / iterations, Is.InRange(0.08f, 0.12f));
        }

        [Test]
        public void GenerateForecast_IsApproximatelyNinetyPercentCorrect()
        {
            const int iterations = 100000;
            var random = new global::System.Random(23456);
            var correct = 0;

            for (var i = 0; i < iterations; i++)
            {
                var forecast = WeatherSystem.GenerateForecast(
                    WeatherType.Cloudy,
                    (float)random.NextDouble(),
                    (float)random.NextDouble());
                if (forecast == WeatherType.Cloudy)
                {
                    correct++;
                }
            }

            Assert.That((float)correct / iterations, Is.InRange(0.88f, 0.92f));
        }

        [Test]
        public void GenerateDifferentWeather_NeverReturnsActualWeather()
        {
            foreach (WeatherType actualWeather in Enum.GetValues(typeof(WeatherType)))
            {
                for (var i = 0; i <= 100; i++)
                {
                    var forecast = WeatherSystem.GenerateDifferentWeather(actualWeather, i / 100f);
                    Assert.That(forecast, Is.Not.EqualTo(actualWeather));
                }
            }
        }

        [Test]
        public void AdvanceToNextMorning_UsesPreviousNextActualWeatherAsCurrentWeather()
        {
            var gameObject = new GameObject("TimeManagerWeatherTest");
            try
            {
                var stateManager = gameObject.AddComponent<ConversationGameStateManager>();
                stateManager.State.SetString(WeatherSystem.CurrentWeatherKey, WeatherType.Sunny.ToString());
                stateManager.State.SetString(WeatherSystem.NextActualWeatherKey, WeatherType.Rainy.ToString());
                stateManager.State.SetString(WeatherSystem.CurrentWeatherForecastKey, WeatherType.Cloudy.ToString());

                var timeManager = gameObject.AddComponent<TimeManager>();
                timeManager.EnsureInitialized();

                var expectedCurrentWeather = timeManager.NextActualWeather;
                timeManager.AdvanceToNextMorning();

                Assert.That(timeManager.Weather, Is.EqualTo(expectedCurrentWeather));
                Assert.That(stateManager.State.TryGetString(WeatherSystem.CurrentWeatherKey, out var currentWeather), Is.True);
                Assert.That(currentWeather, Is.EqualTo(expectedCurrentWeather.ToString()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SyncWithoutDayChange_DoesNotRegenerateWeather()
        {
            var gameObject = new GameObject("TimeManagerStableWeatherTest");
            try
            {
                var stateManager = gameObject.AddComponent<ConversationGameStateManager>();
                stateManager.State.SetString(WeatherSystem.CurrentWeatherKey, WeatherType.Cloudy.ToString());
                stateManager.State.SetString(WeatherSystem.NextActualWeatherKey, WeatherType.Rainy.ToString());
                stateManager.State.SetString(WeatherSystem.CurrentWeatherForecastKey, WeatherType.Sunny.ToString());

                var timeManager = gameObject.AddComponent<TimeManager>();
                timeManager.EnsureInitialized();

                var weather = timeManager.Weather;
                var nextActualWeather = timeManager.NextActualWeather;
                var forecast = timeManager.TomorrowWeather;

                timeManager.AdvanceTime(0);

                Assert.That(timeManager.Weather, Is.EqualTo(weather));
                Assert.That(timeManager.NextActualWeather, Is.EqualTo(nextActualWeather));
                Assert.That(timeManager.TomorrowWeather, Is.EqualTo(forecast));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SetCurrentTimeAndWeather_SyncsExistingConversationState()
        {
            var gameObject = new GameObject("TimeManagerDebugStateTest");
            try
            {
                var stateManager = gameObject.AddComponent<ConversationGameStateManager>();
                var timeManager = gameObject.AddComponent<TimeManager>();

                timeManager.SetCurrentTime(0, DayPeriod.Evening);
                timeManager.SetWeather(WeatherType.Rainy);
                timeManager.SetTomorrowWeather(WeatherType.Cloudy);

                Assert.That(timeManager.CurrentDay, Is.EqualTo(1));
                Assert.That(timeManager.CurrentPeriod, Is.EqualTo(DayPeriod.Evening));
                Assert.That(timeManager.Weather, Is.EqualTo(WeatherType.Rainy));
                Assert.That(timeManager.TomorrowWeather, Is.EqualTo(WeatherType.Cloudy));
                Assert.That(stateManager.State.TryGetInt("CurrentDay", out int day), Is.True);
                Assert.That(day, Is.EqualTo(1));
                Assert.That(stateManager.State.TryGetString("CurrentPhase", out string phase), Is.True);
                Assert.That(phase, Is.EqualTo("Evening"));
                Assert.That(stateManager.State.TryGetString(WeatherSystem.CurrentWeatherKey, out string weather), Is.True);
                Assert.That(weather, Is.EqualTo(WeatherType.Rainy.ToString()));
                Assert.That(stateManager.State.TryGetString(WeatherSystem.CurrentWeatherForecastKey, out string tomorrow), Is.True);
                Assert.That(tomorrow, Is.EqualTo(WeatherType.Cloudy.ToString()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void CalendarDate_UsesSavedStartDateAndClampsOutOfRangeDays()
        {
            bool hadPreviousValue = PlayerPrefs.HasKey(GameStartDateTicksPrefsKey);
            string previousValue = PlayerPrefs.GetString(GameStartDateTicksPrefsKey, string.Empty);
            GameObject gameObject = new GameObject("TimeManagerCalendarDateTest");
            try
            {
                DateTime startDate = new DateTime(2026, 9, 19);
                PlayerPrefs.SetString(
                    GameStartDateTicksPrefsKey,
                    startDate.Ticks.ToString(CultureInfo.InvariantCulture));

                TimeManager timeManager = gameObject.AddComponent<TimeManager>();
                timeManager.SetCurrentTime(3, DayPeriod.Morning);

                Assert.That(timeManager.GameStartDate, Is.EqualTo(startDate));
                Assert.That(timeManager.GetCalendarDate(timeManager.CurrentDay), Is.EqualTo(startDate.AddDays(2)));
                Assert.That(timeManager.GetCalendarDate(0), Is.EqualTo(startDate));
                Assert.That(timeManager.GetCalendarDate(int.MaxValue), Is.EqualTo(DateTime.MaxValue.Date));
            }
            finally
            {
                if (hadPreviousValue)
                {
                    PlayerPrefs.SetString(GameStartDateTicksPrefsKey, previousValue);
                }
                else
                {
                    PlayerPrefs.DeleteKey(GameStartDateTicksPrefsKey);
                }

                PlayerPrefs.Save();
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
    }
}
