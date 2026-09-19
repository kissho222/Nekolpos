using System;
using UnityEngine;

namespace Nekolpos.TimeSystem
{
    public static class WeatherSystem
    {
        public const string WeatherKey = "Weather";
        public const string CurrentWeatherKey = "CurrentWeather";
        public const string TomorrowWeatherKey = "TomorrowWeather";
        public const string CurrentWeatherForecastKey = "CurrentWeatherForecast";
        public const string ForecastWeatherKey = "FORECAST_WEATHER";
        public const string ForecastWeatherMisspelledKey = "FORCAST_WEATHER";
        public const string ForecastWeatherQuestionKey = "FORCAST?WEATHER";
        public const string NextActualWeatherKey = "NextActualWeather";
        public const string ForecastCorrectKey = "WeatherForecastCorrect";

        public static WeatherType GenerateRandomWeather()
        {
            return GenerateRandomWeather(UnityEngine.Random.value);
        }

        public static WeatherType GenerateRandomWeather(float value)
        {
            if (value < 0.7f)
            {
                return WeatherType.Sunny;
            }

            if (value < 0.9f)
            {
                return WeatherType.Cloudy;
            }

            return WeatherType.Rainy;
        }

        public static WeatherType GenerateForecast(WeatherType actualWeather)
        {
            return GenerateForecast(actualWeather, UnityEngine.Random.value, UnityEngine.Random.value);
        }

        public static WeatherType GenerateForecast(WeatherType actualWeather, float correctnessValue, float missValue)
        {
            if (correctnessValue < 0.9f)
            {
                return actualWeather;
            }

            return GenerateDifferentWeather(actualWeather, missValue);
        }

        public static WeatherType GenerateDifferentWeather(WeatherType actualWeather)
        {
            return GenerateDifferentWeather(actualWeather, UnityEngine.Random.value);
        }

        public static WeatherType GenerateDifferentWeather(WeatherType actualWeather, float value)
        {
            switch (actualWeather)
            {
                case WeatherType.Sunny:
                    return value < (0.2f / 0.3f) ? WeatherType.Cloudy : WeatherType.Rainy;
                case WeatherType.Cloudy:
                    return value < (0.7f / 0.8f) ? WeatherType.Sunny : WeatherType.Rainy;
                case WeatherType.Rainy:
                    return value < (0.7f / 0.9f) ? WeatherType.Sunny : WeatherType.Cloudy;
                default:
                    return WeatherType.Sunny;
            }
        }

        public static string ToDisplayText(WeatherType weather)
        {
            switch (weather)
            {
                case WeatherType.Sunny:
                    return "晴れ";
                case WeatherType.Cloudy:
                    return "曇り";
                case WeatherType.Rainy:
                    return "雨";
                default:
                    return weather.ToString();
            }
        }

        public static bool TryParseWeather(string value, out WeatherType weather)
        {
            if (Enum.TryParse(value, false, out weather))
            {
                return true;
            }

            switch (value)
            {
                case "晴れ":
                    weather = WeatherType.Sunny;
                    return true;
                case "曇り":
                    weather = WeatherType.Cloudy;
                    return true;
                case "雨":
                case "Rain":
                    weather = WeatherType.Rainy;
                    return true;
                default:
                    weather = default;
                    return false;
            }
        }
    }
}
