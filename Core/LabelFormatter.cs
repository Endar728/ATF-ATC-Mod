using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace NO_ATC_Mod.Core
{
    /// <summary>
    /// LotATC-style label formatter for aircraft labels.
    /// Supports attributes like %(callsign), %(tn), %(alti_short), etc.
    /// </summary>
    public static class LabelFormatter
    {
        // Regex to match %(attribute) or %(attribute|option) patterns
        private static readonly Regex AttributePattern = new Regex(@"%\((\w+)(?:\|([^)]+))?\)", RegexOptions.Compiled);
        
        /// <summary>
        /// Format a label string with tracked unit data
        /// </summary>
        public static string FormatLabel(string format, TrackedUnit tracked, string callsign, string unitType, float playerAltitude)
        {
            if (string.IsNullOrEmpty(format))
                return callsign;
            
            return AttributePattern.Replace(format, match =>
            {
                string attribute = match.Groups[1].Value.ToLower();
                string option = match.Groups[2].Success ? match.Groups[2].Value : null;
                
                return GetAttributeValue(attribute, option, tracked, callsign, unitType, playerAltitude);
            });
        }
        
        private static string GetAttributeValue(string attribute, string option, TrackedUnit tracked, 
            string callsign, string unitType, float playerAltitude)
        {
            switch (attribute)
            {
                case "callsign":
                case "name":
                    return TruncateIfNeeded(callsign, option);
                
                case "tn":
                case "track_number":
                    return $"TN#{tracked.trackNumber}";
                
                case "alti_short":
                    return FormatAltitudeShort(tracked.altitude, playerAltitude, option);
                
                case "alti_long":
                    return FormatAltitudeLong(tracked.altitude, playerAltitude);
                
                case "gs":
                case "ground_speed":
                    return FormatSpeed(tracked.speed, option);
                
                case "gs_short":
                    return FormatSpeedShort(tracked.speed);
                
                case "heading":
                case "hdg":
                    return $"{tracked.heading:F0}°";
                
                case "type":
                    return TruncateIfNeeded(unitType, option);
                
                case "vert_indic":
                case "vertical_indicator":
                    return GetVerticalIndicator(tracked.verticalTrend, option);
                
                case "climb_speed":
                    return FormatVerticalSpeed(tracked.verticalSpeed);
                
                case "distance":
                case "rng":
                case "range":
                    return FormatDistance(tracked.distance);
                
                case "human":
                    // Indicate if this is a human player
                    bool isHuman = IsHumanPlayer(tracked);
                    if (option == "icon")
                        return isHuman ? "👤" : "";
                    return isHuman ? "HUMAN" : "";
                
                default:
                    return $"%({attribute})"; // Return unchanged if unknown
            }
        }
        
        private static string TruncateIfNeeded(string value, string option)
        {
            if (string.IsNullOrEmpty(option) || string.IsNullOrEmpty(value))
                return value ?? "";
            
            if (int.TryParse(option, out int maxLength) && value.Length > maxLength)
            {
                return value.Substring(0, maxLength);
            }
            
            // Handle "short" option for callsign (e.g., "Viper 11" -> "VR11")
            if (option.ToLower() == "short" && value.Contains(" "))
            {
                var parts = value.Split(' ');
                if (parts.Length >= 2)
                {
                    string prefix = parts[0].Length >= 2 ? parts[0].Substring(0, 2).ToUpper() : parts[0].ToUpper();
                    return prefix + parts[1];
                }
            }
            
            return value;
        }
        
        /// <summary>
        /// Format altitude in short format (FL350 style or abbreviated)
        /// </summary>
        public static string FormatAltitudeShort(float altitudeMeters, float playerAltitude, string option = null)
        {
            float displayAlt = altitudeMeters;
            
            // Check if we should show relative altitude
            if (Plugin.ShowRelativeAltitude.Value)
            {
                displayAlt = altitudeMeters - playerAltitude;
            }
            else
            {
                // Clamp ASL altitude to 0 minimum (no negative altitudes for above sea level)
                displayAlt = Mathf.Max(0f, altitudeMeters);
            }
            
            if (Plugin.UseImperialUnits.Value)
            {
                float feet = displayAlt / 0.3048f;
                
                // Flight Level format (FL350 = 35,000 ft)
                if (Plugin.UseFlightLevel.Value && !Plugin.ShowRelativeAltitude.Value)
                {
                    int flightLevel = Mathf.RoundToInt(feet / 100f);
                    
                    // Option "thousand" shows "35" instead of "350"
                    if (option == "thousand")
                        return $"{flightLevel / 10}";
                    
                    return $"FL{flightLevel:D3}";
                }
                else
                {
                    // Standard abbreviated format
                    string sign = Plugin.ShowRelativeAltitude.Value && displayAlt >= 0 ? "+" : "";
                    if (Mathf.Abs(feet) >= 1000)
                        return $"{sign}{feet / 1000f:F1}k";
                    return $"{sign}{feet:F0}";
                }
            }
            else
            {
                // Metric
                string sign = Plugin.ShowRelativeAltitude.Value && displayAlt >= 0 ? "+" : "";
                if (Mathf.Abs(displayAlt) >= 1000)
                    return $"{sign}{displayAlt / 1000f:F1}km";
                return $"{sign}{displayAlt:F0}m";
            }
        }
        
        /// <summary>
        /// Format altitude in long format with units
        /// </summary>
        public static string FormatAltitudeLong(float altitudeMeters, float playerAltitude)
        {
            float displayAlt = altitudeMeters;
            string prefix = "";
            
            if (Plugin.ShowRelativeAltitude.Value)
            {
                displayAlt = altitudeMeters - playerAltitude;
                prefix = displayAlt >= 0 ? "+" : "";
            }
            else
            {
                // Clamp ASL altitude to 0 minimum (no negative altitudes for above sea level)
                displayAlt = Mathf.Max(0f, altitudeMeters);
            }
            
            if (Plugin.UseImperialUnits.Value)
            {
                float feet = displayAlt / 0.3048f;
                return $"{prefix}{feet:F0} ft";
            }
            else
            {
                return $"{prefix}{displayAlt:F0} m";
            }
        }
        
        public static string FormatSpeed(float metersPerSecond, string option = null)
        {
            if (Plugin.UseImperialUnits.Value)
            {
                float knots = metersPerSecond / 0.514444f;
                return $"{knots:F0} kt";
            }
            else
            {
                float kmh = metersPerSecond * 3.6f;
                return $"{kmh:F0} km/h";
            }
        }
        
        public static string FormatSpeedShort(float metersPerSecond)
        {
            if (Plugin.UseImperialUnits.Value)
            {
                float knots = metersPerSecond / 0.514444f;
                return $"{knots:F0}";
            }
            else
            {
                float kmh = metersPerSecond * 3.6f;
                return $"{kmh:F0}";
            }
        }
        
        public static string FormatDistance(float meters)
        {
            if (Plugin.UseImperialUnits.Value)
            {
                float nm = meters / 1852f;
                return $"{nm:F1}nm";
            }
            else
            {
                float km = meters / 1000f;
                return $"{km:F1}km";
            }
        }
        
        public static string FormatVerticalSpeed(float metersPerSecond)
        {
            if (Plugin.UseImperialUnits.Value)
            {
                float fpm = metersPerSecond * 196.85f; // m/s to ft/min
                string sign = fpm >= 0 ? "+" : "";
                return $"{sign}{fpm:F0} fpm";
            }
            else
            {
                string sign = metersPerSecond >= 0 ? "+" : "";
                return $"{sign}{metersPerSecond:F1} m/s";
            }
        }
        
        /// <summary>
        /// Get vertical indicator character
        /// </summary>
        public static string GetVerticalIndicator(VerticalTrend trend, string option = null)
        {
            // Different arrow styles based on option
            int style = 0;
            bool noStable = false;
            
            if (!string.IsNullOrEmpty(option))
            {
                if (option.Contains("nostable"))
                    noStable = true;
                
                if (option.Contains("0")) style = 0;
                else if (option.Contains("1")) style = 1;
                else if (option.Contains("2")) style = 2;
            }
            
            switch (trend)
            {
                case VerticalTrend.Climbing:
                    switch (style)
                    {
                        case 0: return "↑";
                        case 1: return "▲";
                        case 2: return "⬆";
                        default: return "↑";
                    }
                
                case VerticalTrend.Descending:
                    switch (style)
                    {
                        case 0: return "↓";
                        case 1: return "▼";
                        case 2: return "⬇";
                        default: return "↓";
                    }
                
                case VerticalTrend.Stable:
                default:
                    if (noStable) return "";
                    switch (style)
                    {
                        case 0: return "—";
                        case 1: return "●";
                        case 2: return "═";
                        default: return "—";
                    }
            }
        }
        
        private static bool IsHumanPlayer(TrackedUnit tracked)
        {
            if (tracked.unit == null) return false;
            
            Aircraft aircraft = tracked.unit as Aircraft;
            if (aircraft == null) return false;
            
            try
            {
                if (aircraft.pilots != null && aircraft.pilots.Length > 0 && aircraft.pilots[0] != null)
                {
                    var pilot = aircraft.pilots[0];
                    var playerField = pilot.GetType().GetField("player", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (playerField != null)
                    {
                        var player = playerField.GetValue(pilot);
                        return player != null;
                    }
                }
            }
            catch
            {
                // Ignore reflection errors
            }
            
            return false;
        }
    }
}
