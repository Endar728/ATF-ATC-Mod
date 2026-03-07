using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using NO_ATC_Mod.Core;

namespace NO_ATC_Mod.UI
{
    public class ATCWindow : MonoBehaviour
    {
        private bool showWindow = false;
        private Rect windowRect;
        private Vector2 scrollPosition;
        private GUIStyle windowStyle;
        private GUIStyle labelStyle;
        private GUIStyle headerStyle;
        private GUIStyle dataStyle;
        private GUIStyle headerRowStyle;
        private bool stylesInitialized = false;
        
        private const int ColCallsign = 140;
        private const int ColTN = 50;
        private const int ColRng = 56;
        private const int ColBrg = 40;
        private const int ColAlt = 64;
        private const int ColVert = 20;
        private const int ColHdg = 40;
        private const int ColSpd = 48;
        
        private Dictionary<Aircraft, string> callsignCache = new Dictionary<Aircraft, string>();
        private TrackedUnit? selectedTarget1 = null;
        private TrackedUnit? selectedTarget2 = null;
        
        // Settings panel state
        private bool showSettingsPanel = false;
        private Vector2 settingsScrollPosition = Vector2.zero;
        
        private int lastToggleFrame = -1;
        
        // Key selection state
        private bool waitingForToggleKey = false;
        
        private string FormatDistance(float meters)
        {
            if (Plugin.UseImperialUnits.Value)
            {
                float nauticalMiles = meters / 1852f;
                return $"{nauticalMiles:F2} nm";
            }
            else
            {
                float kilometers = meters / 1000f;
                return $"{kilometers:F2} km";
            }
        }
        
        private string FormatAltitude(float meters, float playerAltitude = 0f)
        {
            // Use the new LabelFormatter for consistent altitude formatting
            return LabelFormatter.FormatAltitudeLong(meters, playerAltitude);
        }
        
        private string FormatAltitudeShort(float meters, float playerAltitude = 0f)
        {
            return LabelFormatter.FormatAltitudeShort(meters, playerAltitude, null);
        }
        
        private string FormatSpeed(float metersPerSecond)
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
        
        private static float BearingFrom(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            float b = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            if (b < 0f) b += 360f;
            return b;
        }
        
        private string FormatBearing(float deg) => $"{deg:F0}°";
        private string FormatRngCell(float meters) => FormatDistance(meters);
        private string FormatAltCell(float meters) => FormatAltitude(meters);
        private string FormatSpdCell(float mps) => FormatSpeed(mps);

        void Awake()
        {
            float x = Mathf.Clamp(Plugin.WindowPosX.Value, 0, Screen.width - Plugin.WindowWidth.Value);
            float y = Mathf.Clamp(Plugin.WindowPosY.Value, 0, Screen.height - Plugin.WindowHeight.Value);
            
            // If position is invalid (negative or off-screen), use default position
            if (x < 0 || y < 0 || x > Screen.width || y > Screen.height)
            {
                x = 10f;
                y = 10f;
            }
            
            windowRect = new Rect(x, y, Plugin.WindowWidth.Value, Plugin.WindowHeight.Value);
            showWindow = Plugin.ShowATCWindow.Value;
        }

        public void Update()
        {
            // Handle key selection waiting states
            if (waitingForToggleKey)
            {
                foreach (KeyCode keyCode in System.Enum.GetValues(typeof(KeyCode)))
                {
                    if (UnityEngine.Input.GetKeyDown(keyCode))
                    {
                        if (keyCode >= KeyCode.Mouse0 && keyCode <= KeyCode.Mouse6) continue;
                        if (keyCode == KeyCode.None) continue;
                        Plugin.ToggleWindowKey.Value = keyCode;
                        waitingForToggleKey = false;
                        Plugin.Log.LogInfo($"[ATCWindow] Toggle key set to: {keyCode}");
                        break;
                    }
                }
            }

            CheckToggleKey();

            if (UnityEngine.Input.GetKeyDown(KeyCode.F))
            {
                Plugin.ShowFriendly.Value = !Plugin.ShowFriendly.Value;
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.H))
            {
                Plugin.ShowHostile.Value = !Plugin.ShowHostile.Value;
            }

            windowRect.width = Plugin.WindowWidth.Value;
            windowRect.height = Plugin.WindowHeight.Value;
            windowRect.x = Mathf.Clamp(Plugin.WindowPosX.Value, 0, Screen.width - windowRect.width);
            windowRect.y = Mathf.Clamp(Plugin.WindowPosY.Value, 0, Screen.height - windowRect.height);
        }

        void OnGUI()
        {
            CheckToggleKey();

            if (!showWindow || !Plugin.Enabled.Value)
            {
                return;
            }
            
            if (windowRect.x < 0 || windowRect.y < 0 || 
                windowRect.x > Screen.width - windowRect.width || 
                windowRect.y > Screen.height - windowRect.height)
            {
                windowRect.x = Mathf.Clamp(windowRect.x, 0, Screen.width - windowRect.width);
                windowRect.y = Mathf.Clamp(windowRect.y, 0, Screen.height - windowRect.height);
            }

            if (!stylesInitialized)
            {
                InitializeStyles();
            }
            
            try
            {
                windowRect = GUILayout.Window(
                    12345,
                    windowRect,
                    DrawWindow,
                    "ATC Radar Display",
                    windowStyle
                );
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[ATCWindow] Error in OnGUI: {ex.Message}\n{ex.StackTrace}");
            }

            Plugin.WindowPosX.Value = windowRect.x;
            Plugin.WindowPosY.Value = windowRect.y;
        }

        private GUIStyle toggleBoxStyle;
        private GUIStyle toggleBoxFriendlyStyle;
        private GUIStyle toggleBoxHostileStyle;
        private GUIStyle checkboxStyle;
        private GUIStyle checkboxStyleFriendly;
        private GUIStyle checkboxStyleHostile;
        private Texture2D windowBgSolid;
        private Texture2D windowBgTransparent;
        private Texture2D contentBgSolid;
        private Texture2D contentBgTransparent;

        private void InitializeStyles()
        {
            windowBgSolid = MakeTex(2, 2, new Color(0.04f, 0.04f, 0.06f, 1f));
            windowBgTransparent = MakeTex(2, 2, new Color(0.06f, 0.06f, 0.08f, 0.88f));
            contentBgSolid = MakeTex(2, 2, new Color(0.05f, 0.05f, 0.07f, 1f));
            contentBgTransparent = MakeTex(2, 2, new Color(0.06f, 0.06f, 0.09f, 0.85f));

            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = windowBgSolid;

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.normal.textColor = Plugin.ATCColor.Value;
            labelStyle.fontSize = 14;

            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.normal.textColor = Color.white;
            headerStyle.fontSize = 16;
            headerStyle.fontStyle = FontStyle.Bold;

            dataStyle = new GUIStyle(GUI.skin.label);
            dataStyle.normal.textColor = new Color(0.95f, 0.98f, 0.95f);
            dataStyle.fontSize = 14;
            dataStyle.alignment = TextAnchor.MiddleLeft;

            headerRowStyle = new GUIStyle(GUI.skin.label);
            headerRowStyle.normal.textColor = Color.white;
            headerRowStyle.fontSize = 13;
            headerRowStyle.fontStyle = FontStyle.Bold;

            contentBgStyle = new GUIStyle(GUI.skin.box);
            contentBgStyle.normal.background = contentBgSolid;
            contentBgStyle.padding = new RectOffset(6, 6, 4, 4);

            toggleBoxStyle = new GUIStyle(GUI.skin.button);
            toggleBoxStyle.fontSize = 14;
            toggleBoxStyle.padding = new RectOffset(10, 6, 4, 4);
            toggleBoxStyle.normal.background = MakeTex(2, 2, new Color(0.18f, 0.18f, 0.22f, 1f));
            toggleBoxStyle.hover.background = MakeTex(2, 2, new Color(0.25f, 0.25f, 0.3f, 1f));
            toggleBoxStyle.active.background = MakeTex(2, 2, new Color(0.12f, 0.12f, 0.16f, 1f));

            toggleBoxFriendlyStyle = new GUIStyle(toggleBoxStyle);
            toggleBoxFriendlyStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            toggleBoxFriendlyStyle.normal.background = MakeTex(2, 2, new Color(0.2f, 0.2f, 0.22f, 1f));
            toggleBoxFriendlyStyle.hover.textColor = new Color(0.65f, 0.65f, 0.65f, 1f);
            toggleBoxFriendlyStyle.hover.background = MakeTex(2, 2, new Color(0.26f, 0.26f, 0.28f, 1f));
            toggleBoxFriendlyStyle.active.textColor = Color.green;
            toggleBoxFriendlyStyle.onNormal = new GUIStyleState();
            toggleBoxFriendlyStyle.onNormal.textColor = new Color(0.5f, 1f, 0.55f, 1f);
            toggleBoxFriendlyStyle.onNormal.background = MakeTex(2, 2, new Color(0.08f, 0.22f, 0.1f, 1f));
            toggleBoxFriendlyStyle.onHover = new GUIStyleState();
            toggleBoxFriendlyStyle.onHover.textColor = new Color(0.7f, 1f, 0.75f, 1f);
            toggleBoxFriendlyStyle.onHover.background = MakeTex(2, 2, new Color(0.1f, 0.28f, 0.12f, 1f));
            toggleBoxFriendlyStyle.onActive = toggleBoxFriendlyStyle.onNormal;

            toggleBoxHostileStyle = new GUIStyle(toggleBoxStyle);
            toggleBoxHostileStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            toggleBoxHostileStyle.normal.background = MakeTex(2, 2, new Color(0.22f, 0.2f, 0.2f, 1f));
            toggleBoxHostileStyle.hover.textColor = new Color(0.65f, 0.65f, 0.65f, 1f);
            toggleBoxHostileStyle.hover.background = MakeTex(2, 2, new Color(0.28f, 0.26f, 0.26f, 1f));
            toggleBoxHostileStyle.active.textColor = Color.red;
            toggleBoxHostileStyle.onNormal = new GUIStyleState();
            toggleBoxHostileStyle.onNormal.textColor = new Color(1f, 0.5f, 0.5f, 1f);
            toggleBoxHostileStyle.onNormal.background = MakeTex(2, 2, new Color(0.22f, 0.08f, 0.08f, 1f));
            toggleBoxHostileStyle.onHover = new GUIStyleState();
            toggleBoxHostileStyle.onHover.textColor = new Color(1f, 0.65f, 0.65f, 1f);
            toggleBoxHostileStyle.onHover.background = MakeTex(2, 2, new Color(0.28f, 0.1f, 0.1f, 1f));
            toggleBoxHostileStyle.onActive = toggleBoxHostileStyle.onNormal;

            checkboxStyle = new GUIStyle(GUI.skin.box);
            checkboxStyle.padding = new RectOffset(3, 3, 3, 3);
            checkboxStyle.border = new RectOffset(3, 3, 3, 3);
            checkboxStyle.alignment = TextAnchor.MiddleCenter;
            checkboxStyle.fontSize = 16;
            Color uncheckedFill = new Color(0.52f, 0.52f, 0.58f, 1f);
            Color uncheckedEdge = new Color(0.72f, 0.72f, 0.78f, 1f);
            checkboxStyle.normal.background = MakeTexWithBorder(20, 20, uncheckedFill, uncheckedEdge, 3);
            checkboxStyle.normal.textColor = new Color(0.25f, 0.25f, 0.3f, 1f);
            checkboxStyle.onNormal = new GUIStyleState();
            checkboxStyle.onNormal.background = MakeTexWithBorder(20, 20, uncheckedFill, uncheckedEdge, 3);
            checkboxStyle.onNormal.textColor = new Color(0.25f, 0.25f, 0.3f, 1f);

            Color checkedFill = new Color(0.65f, 0.65f, 0.7f, 1f);
            Color checkedEdge = new Color(0.88f, 0.88f, 0.92f, 1f);
            checkboxStyleFriendly = new GUIStyle(checkboxStyle);
            checkboxStyleFriendly.normal.background = MakeTexWithBorder(20, 20, uncheckedFill, uncheckedEdge, 3);
            checkboxStyleFriendly.onNormal.background = MakeTexWithBorder(20, 20, checkedFill, checkedEdge, 3);
            checkboxStyleFriendly.onNormal.textColor = new Color(0.2f, 0.2f, 0.25f, 1f);

            checkboxStyleHostile = new GUIStyle(checkboxStyle);
            checkboxStyleHostile.normal.background = MakeTexWithBorder(20, 20, uncheckedFill, uncheckedEdge, 3);
            checkboxStyleHostile.onNormal.background = MakeTexWithBorder(20, 20, checkedFill, checkedEdge, 3);
            checkboxStyleHostile.onNormal.textColor = new Color(0.2f, 0.2f, 0.25f, 1f);

            stylesInitialized = true;
        }

        private GUIStyle contentBgStyle;
        private static GUIStyle _rowBoxStyle;
        private static GUIStyle RowBoxStyle
        {
            get
            {
                if (_rowBoxStyle == null)
                {
                    _rowBoxStyle = new GUIStyle(GUI.skin.box);
                    _rowBoxStyle.normal.background = MakeTexStatic(2, 2, new Color(0.08f, 0.08f, 0.1f, 1f));
                }
                return _rowBoxStyle;
            }
        }

        private static Texture2D MakeTexStatic(int w, int h, Color col)
        {
            Color[] pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var t = new Texture2D(w, h);
            t.SetPixels(pix);
            t.Apply();
            return t;
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;

            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private Texture2D MakeTexWithBorder(int w, int h, Color center, Color border, int borderPx)
        {
            var tex = new Texture2D(w, h);
            var pix = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool edge = x < borderPx || x >= w - borderPx || y < borderPx || y >= h - borderPx;
                pix[y * w + x] = edge ? border : center;
            }
            tex.SetPixels(pix);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private void DrawWindow(int windowID)
        {
            bool transparent = Plugin.WindowTransparent != null && Plugin.WindowTransparent.Value;
            if (windowStyle != null && windowBgSolid != null && windowBgTransparent != null)
            {
                windowStyle.normal.background = transparent ? windowBgTransparent : windowBgSolid;
            }
            if (contentBgStyle != null && contentBgSolid != null && contentBgTransparent != null)
            {
                contentBgStyle.normal.background = transparent ? contentBgTransparent : contentBgSolid;
            }

            GUILayout.BeginVertical();
            try
            {
                var radarSystem = ATCComponent.GetRadarSystem();
                if (radarSystem == null)
                {
                    GUILayout.Label("Radar system not initialized", labelStyle);
                    GUILayout.EndVertical();
                    GUI.DragWindow();
                    return;
                }

                var trackedUnits = radarSystem.GetTrackedUnits();
                var playerAircraft = GetPlayerAircraft();

                if (playerAircraft == null)
                {
                    GUILayout.Label("No player aircraft", labelStyle);
                    GUILayout.EndVertical();
                    GUI.DragWindow();
                    return;
                }

                Vector3 playerPos = playerAircraft.rb != null ? playerAircraft.rb.transform.position : Vector3.zero;
                Vector3 playerVel = playerAircraft.rb != null ? playerAircraft.rb.velocity : Vector3.zero;

                var oldColor = GUI.color;
                GUILayout.BeginVertical(contentBgStyle);
                
                // AWACS Mode indicator (prominent banner when active)
                var radarSystemRef = ATCComponent.GetRadarSystem();
                if (radarSystemRef != null && radarSystemRef.ReferenceUnit != null)
                {
                    GUI.backgroundColor = new Color(0.2f, 0.4f, 0.6f, 1f);
                    GUILayout.BeginHorizontal(RowBoxStyle);
                    GUI.color = Color.yellow;
                    GUILayout.Label($"★ AWACS MODE ★ Reference: {radarSystemRef.ReferenceUnit.gameObject.name}", headerRowStyle);
                    GUI.color = oldColor;
                    if (GUILayout.Button("X", GUILayout.Width(24), GUILayout.Height(20)))
                    {
                        radarSystemRef.ReferenceUnit = null;
                        Plugin.Log.LogInfo("[ATCWindow] AWACS reference cleared");
                    }
                    GUILayout.EndHorizontal();
                    GUI.backgroundColor = Color.white;
                    GUILayout.Space(2);
                }
                
                GUILayout.BeginHorizontal();
                GUILayout.Label("ATC", headerStyle, GUILayout.Width(36));
                GUILayout.Label($"{trackedUnits.Count} ct", dataStyle, GUILayout.Width(40));
                GUILayout.Label(FormatDistance(Plugin.RadarRange.Value), dataStyle, GUILayout.Width(ColRng));
                GUILayout.Space(12);
                GUILayout.Label("Show", labelStyle, GUILayout.Width(36));
                GUIStyle chkF = checkboxStyleFriendly ?? GUI.skin.toggle;
                GUIStyle chkH = checkboxStyleHostile ?? GUI.skin.toggle;
                GUILayout.BeginHorizontal(GUILayout.Width(0), GUILayout.ExpandWidth(false));
                Plugin.ShowFriendly.Value = GUILayout.Toggle(Plugin.ShowFriendly.Value, Plugin.ShowFriendly.Value ? "\u2713" : "", chkF, GUILayout.Width(30), GUILayout.Height(30));
                GUILayout.Space(4);
                GUILayout.Label("Friendly", labelStyle, GUILayout.Height(30));
                GUILayout.Space(14);
                Plugin.ShowHostile.Value = GUILayout.Toggle(Plugin.ShowHostile.Value, Plugin.ShowHostile.Value ? "\u2713" : "", chkH, GUILayout.Width(30), GUILayout.Height(30));
                GUILayout.Space(4);
                GUILayout.Label("Hostile", labelStyle, GUILayout.Height(30));
                GUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
                if (Plugin.WindowTransparent != null)
                {
                    bool trans = GUILayout.Toggle(Plugin.WindowTransparent.Value, Plugin.WindowTransparent.Value ? " Transparent " : " Solid BG ", toggleBoxStyle ?? GUI.skin.button, GUILayout.Width(88), GUILayout.Height(26));
                    if (trans != Plugin.WindowTransparent.Value)
                        Plugin.WindowTransparent.Value = trans;
                }
                if (GUILayout.Button(showSettingsPanel ? " Settings ▼ " : " Settings ", toggleBoxStyle ?? GUI.skin.button, GUILayout.Width(88), GUILayout.Height(26)))
                    showSettingsPanel = !showSettingsPanel;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Types", labelStyle, GUILayout.Width(44));
                GUIStyle typeBox = toggleBoxStyle ?? GUI.skin.button;
                Plugin.ShowAircraft.Value = GUILayout.Toggle(Plugin.ShowAircraft.Value, " Aircraft ", typeBox, GUILayout.Width(80), GUILayout.Height(26));
                Plugin.ShowShips.Value = GUILayout.Toggle(Plugin.ShowShips.Value, " Ships ", typeBox, GUILayout.Width(68), GUILayout.Height(26));
                Plugin.ShowGroundVehicles.Value = GUILayout.Toggle(Plugin.ShowGroundVehicles.Value, " Ground ", typeBox, GUILayout.Width(76), GUILayout.Height(26));
                Plugin.ShowBuildings.Value = GUILayout.Toggle(Plugin.ShowBuildings.Value, " Buildings ", typeBox, GUILayout.Width(84), GUILayout.Height(26));
                GUILayout.EndHorizontal();
                GUILayout.Space(6);

                if (showSettingsPanel)
                {
                    DrawSettingsPanel();
                    GUILayout.Space(4);
                }
                if (selectedTarget1 != null && selectedTarget2 != null)
                {
                    try
                    {
                        var interBRAA = RadarSystem.CalculateBRAABetween(
                            selectedTarget1.position, selectedTarget1.velocity,
                            selectedTarget2.position, selectedTarget2.altitude);
                        float d = Vector3.Distance(selectedTarget1.position, selectedTarget2.position);
                        GUI.color = Color.cyan;
                        GUILayout.Label($"INT: {FormatDistance(d)}  Brg {interBRAA.Bearing:F0}°  ΔAlt {(interBRAA.AltitudeDiff >= 0 ? "+" : "")}{FormatAltitude(Mathf.Abs(interBRAA.AltitudeDiff))}  Asp {interBRAA.Aspect:F0}°", dataStyle);
                        GUI.color = oldColor;
                        if (GUILayout.Button("Clear selection", GUILayout.Width(100), GUILayout.Height(16)))
                        { selectedTarget1 = null; selectedTarget2 = null; }
                    }
                    catch (Exception) { }
                }
                else if (selectedTarget1 != null && Plugin.ShowBRAA.Value)
                {
                    // Use reference point (AWACS mode) if set, otherwise use player
                    var radarSys = ATCComponent.GetRadarSystem();
                    var (refPos, refVel) = radarSys != null ? radarSys.GetReferencePoint() : (playerPos, playerVel);
                    bool isAwacsMode = radarSys?.ReferenceUnit != null;
                    string refLabel = isAwacsMode ? "[AWACS] " : "";
                    
                    var braa = selectedTarget1.CalculateBRAA(refPos, refVel);
                    string altDiff = braa.AltitudeDiff >= 0 ? "+" : "";
                    GUI.color = Color.cyan;
                    GUILayout.Label($"{refLabel}BRAA {braa.Bearing:F0}° / {FormatDistance(braa.Range)} / {FormatAltitudeShort(braa.Altitude, refPos.y)} {altDiff}{FormatAltitudeShort(Mathf.Abs(braa.AltitudeDiff), 0)} / Asp {braa.Aspect:F0}°", dataStyle);
                    GUI.color = oldColor;
                    
                    // Quick button to set selected as AWACS reference
                    if (radarSys != null && selectedTarget1.unit != radarSys.ReferenceUnit)
                    {
                        if (GUILayout.Button("Set Ref", GUILayout.Width(52), GUILayout.Height(16)))
                        {
                            radarSys.ReferenceUnit = selectedTarget1.unit;
                            Plugin.Log.LogInfo($"[ATCWindow] AWACS reference set to: {selectedTarget1.unit?.gameObject.name}");
                        }
                    }
                    
                    if (GUILayout.Button("Clear", GUILayout.Width(44), GUILayout.Height(16)))
                        selectedTarget1 = null;
                }
                else if (selectedTarget1 != null)
                {
                    if (GUILayout.Button("Clear selection", GUILayout.Width(90), GUILayout.Height(16)))
                        selectedTarget1 = null;
                }

                GUILayout.Space(2);
                // ---- Column headers ----
                GUILayout.BeginHorizontal();
                GUILayout.Label("CALLSIGN", headerRowStyle, GUILayout.Width(ColCallsign));
                if (Plugin.ShowTrackNumbers.Value)
                    GUILayout.Label("TN#", headerRowStyle, GUILayout.Width(ColTN));
                GUILayout.Label("RNG", headerRowStyle, GUILayout.Width(ColRng));
                GUILayout.Label("BRG", headerRowStyle, GUILayout.Width(ColBrg));
                GUILayout.Label("ALT", headerRowStyle, GUILayout.Width(ColAlt));
                GUILayout.Label("", headerRowStyle, GUILayout.Width(ColVert)); // Vertical indicator
                GUILayout.Label("HDG", headerRowStyle, GUILayout.Width(ColHdg));
                GUILayout.Label("SPD", headerRowStyle, GUILayout.Width(ColSpd));
                GUILayout.EndHorizontal();

                scrollPosition = GUILayout.BeginScrollView(scrollPosition);
                try
                {
                    var filteredUnits = trackedUnits.Where(u =>
                        (u.isFriendly && Plugin.ShowFriendly.Value) ||
                        (!u.isFriendly && Plugin.ShowHostile.Value)).ToList();
                    
                    List<TrackedUnit> sortedUnits;
                    if (Plugin.StableSort.Value)
                    {
                        // Sort by callsign for stable ordering (doesn't jump around as units move)
                        sortedUnits = filteredUnits.OrderBy(u => GetUnitDisplayName(u)).ToList();
                    }
                    else
                    {
                        // Sort by distance (classic behavior, but list reorders frequently)
                        sortedUnits = filteredUnits.OrderBy(u => u.distance).ToList();
                    }

                    if (sortedUnits.Count == 0)
                    {
                        GUILayout.Label(trackedUnits.Count == 0 ? "No contacts" : "No match (check F/H and type filters)", dataStyle);
                    }
                    else
                    {
                        foreach (var tracked in sortedUnits)
                        {
                            bool isSel1 = selectedTarget1 != null && selectedTarget1.unit == tracked.unit;
                            bool isSel2 = selectedTarget2 != null && selectedTarget2.unit == tracked.unit;
                            bool isSel = isSel1 || isSel2;
                            float brg = BearingFrom(playerPos, tracked.position);

                            if (isSel) GUI.backgroundColor = isSel1 ? new Color(0.25f, 0.5f, 0.6f) : new Color(0.5f, 0.5f, 0.25f);
                            GUILayout.BeginHorizontal(RowBoxStyle, GUILayout.Height(22));
                            if (isSel) GUI.backgroundColor = Color.white;

                            string call = GetUnitDisplayName(tracked);
                            if (call.Length > 12) call = call.Substring(0, 10) + "..";
                            GUI.color = tracked.isFriendly ? Color.green : Color.red;
                            if (GUILayout.Button(call, dataStyle, GUILayout.Width(ColCallsign), GUILayout.Height(18)))
                            {
                                if (isSel1) selectedTarget1 = null;
                                else if (isSel2) selectedTarget2 = null;
                                else if (selectedTarget1 == null) selectedTarget1 = tracked;
                                else if (selectedTarget2 == null) selectedTarget2 = tracked;
                                else { selectedTarget1 = tracked; selectedTarget2 = null; }
                            }
                            GUI.color = oldColor;

                            // Track Number column
                            if (Plugin.ShowTrackNumbers.Value)
                                GUILayout.Label(tracked.trackNumber.ToString(), dataStyle, GUILayout.Width(ColTN));
                            
                            GUILayout.Label(FormatRngCell(tracked.distance), dataStyle, GUILayout.Width(ColRng));
                            GUILayout.Label(FormatBearing(brg), dataStyle, GUILayout.Width(ColBrg));
                            GUILayout.Label(FormatAltitudeShort(tracked.altitude, playerPos.y), dataStyle, GUILayout.Width(ColAlt));
                            
                            // Vertical trend indicator
                            string vertIndicator = LabelFormatter.GetVerticalIndicator(tracked.verticalTrend, null);
                            Color vertColor = tracked.verticalTrend == VerticalTrend.Climbing ? Color.cyan :
                                             tracked.verticalTrend == VerticalTrend.Descending ? Color.yellow : Color.gray;
                            GUI.color = vertColor;
                            GUILayout.Label(vertIndicator, dataStyle, GUILayout.Width(ColVert));
                            GUI.color = oldColor;
                            
                            GUILayout.Label(FormatBearing(tracked.heading), dataStyle, GUILayout.Width(ColHdg));
                            GUILayout.Label(FormatSpdCell(tracked.speed), dataStyle, GUILayout.Width(ColSpd));
                            GUILayout.EndHorizontal();
                            GUILayout.Space(1);
                        }
                    }
                }
                finally
                {
                    GUILayout.EndScrollView();
                }

                GUILayout.EndVertical(); // contentBgStyle
                GUILayout.EndVertical();
                GUI.DragWindow();
            }
            catch (System.TypeLoadException ex)
            {
                Plugin.Log.LogError($"[ATCWindow] TypeLoadException in DrawWindow: {ex.Message}\nTypeName: {ex.TypeName}\n{ex.StackTrace}");
                GUILayout.Label($"Error: Type load failed - {ex.TypeName}", labelStyle);
                GUILayout.EndVertical();
                GUI.DragWindow();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ATCWindow] Error in DrawWindow: {ex.Message}\nType: {ex.GetType().Name}\n{ex.StackTrace}");
                GUILayout.Label($"Error: {ex.GetType().Name} - {ex.Message}", labelStyle);
                GUILayout.EndVertical();
                GUI.DragWindow();
            }
        }

        private void CheckToggleKey()
        {
            if (waitingForToggleKey)
            {
                return;
            }
            
            int currentFrame = Time.frameCount;
            if (UnityEngine.Input.GetKeyDown(Plugin.ToggleWindowKey.Value) && lastToggleFrame != currentFrame)
            {
                lastToggleFrame = currentFrame;
                showWindow = !showWindow;
                Plugin.ShowATCWindow.Value = showWindow;
                Plugin.Log.LogInfo($"[ATCWindow] Window toggled: {(showWindow ? "SHOWN" : "HIDDEN")} (Key: {Plugin.ToggleWindowKey.Value})");
                
                if (showWindow)
                {
                    windowRect.x = Mathf.Clamp(windowRect.x, 0, Screen.width - windowRect.width);
                    windowRect.y = Mathf.Clamp(windowRect.y, 0, Screen.height - windowRect.height);
                }
            }
        }
        
        private void DrawSettingsPanel()
        {
            var oldColor = GUI.color;
            GUI.color = Color.cyan;
            GUILayout.Label("=== SETTINGS ===", headerStyle);
            GUI.color = oldColor;
            
            settingsScrollPosition = GUILayout.BeginScrollView(settingsScrollPosition, GUILayout.Height(400));
            
            try
            {
                GUILayout.Label("General Settings:", headerStyle);
                Plugin.Enabled.Value = GUILayout.Toggle(Plugin.Enabled.Value, "Enable ATC Mod");
                Plugin.ShowATCWindow.Value = GUILayout.Toggle(Plugin.ShowATCWindow.Value, "Show ATC Window");
                if (!Plugin.ShowATCWindow.Value && showWindow)
                {
                    showWindow = false;
                }
                else if (Plugin.ShowATCWindow.Value && !showWindow)
                {
                    showWindow = true;
                }
                
                GUILayout.BeginHorizontal();
                GUILayout.Label("Toggle Key:", labelStyle, GUILayout.Width(100));
                string keyName = Plugin.ToggleWindowKey.Value.ToString();
                if (waitingForToggleKey)
                {
                    GUI.color = Color.yellow;
                    if (GUILayout.Button($"Press any key...", GUILayout.Width(150)))
                    {
                        waitingForToggleKey = false;
                    }
                    GUI.color = oldColor;
                }
                else
                {
                    if (GUILayout.Button($"Key: {keyName}", GUILayout.Width(150)))
                    {
                        waitingForToggleKey = true;
                    }
                }
                GUILayout.EndHorizontal();
                
                GUILayout.Space(5);
                
                GUILayout.Label("Display Settings:", headerStyle);
                
                Plugin.ShowBRAA.Value = GUILayout.Toggle(Plugin.ShowBRAA.Value, "Show BRAA Information");
                Plugin.ShowUnitInfo.Value = GUILayout.Toggle(Plugin.ShowUnitInfo.Value, "Show Unit Info");
                
                GUILayout.BeginHorizontal();
                GUILayout.Label("ATC Color:", labelStyle, GUILayout.Width(100));
                Color currentColor = Plugin.ATCColor.Value;
                GUILayout.BeginVertical();
                GUILayout.BeginHorizontal();
                GUILayout.Label("R:", labelStyle, GUILayout.Width(15));
                float r = GUILayout.HorizontalSlider(currentColor.r, 0f, 1f, GUILayout.Width(80));
                GUILayout.Label($"{r:F2}", labelStyle, GUILayout.Width(40));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("G:", labelStyle, GUILayout.Width(15));
                float g = GUILayout.HorizontalSlider(currentColor.g, 0f, 1f, GUILayout.Width(80));
                GUILayout.Label($"{g:F2}", labelStyle, GUILayout.Width(40));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("B:", labelStyle, GUILayout.Width(15));
                float b = GUILayout.HorizontalSlider(currentColor.b, 0f, 1f, GUILayout.Width(80));
                GUILayout.Label($"{b:F2}", labelStyle, GUILayout.Width(40));
                GUILayout.EndHorizontal();
                if (r != currentColor.r || g != currentColor.g || b != currentColor.b)
                {
                    Plugin.ATCColor.Value = new Color(r, g, b, 1f);
                    // Update label style color
                    if (labelStyle != null)
                    {
                        labelStyle.normal.textColor = Plugin.ATCColor.Value;
                    }
                    if (headerStyle != null)
                    {
                        headerStyle.normal.textColor = Plugin.ATCColor.Value;
                    }
                }
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                
                GUILayout.Space(5);
                
                GUILayout.Label("Unit System:", headerStyle);
                Plugin.UseImperialUnits.Value = GUILayout.Toggle(Plugin.UseImperialUnits.Value, "Use Imperial Units (nm, ft, kt)");
                
                GUILayout.Space(5);
                
                GUILayout.Label("Altitude Display:", headerStyle);
                Plugin.UseFlightLevel.Value = GUILayout.Toggle(Plugin.UseFlightLevel.Value, "Use Flight Level Format (FL350 style)");
                Plugin.ShowRelativeAltitude.Value = GUILayout.Toggle(Plugin.ShowRelativeAltitude.Value, "Show Relative Altitude (vs your aircraft)");
                
                GUILayout.Space(5);
                
                GUILayout.Label("Track Numbers & Labels:", headerStyle);
                Plugin.ShowTrackNumbers.Value = GUILayout.Toggle(Plugin.ShowTrackNumbers.Value, "Show Track Numbers (TN#)");
                
                GUILayout.Space(5);
                
                // AWACS Mode - Reference Point Selection
                GUILayout.Label("AWACS Mode (Reference Point):", headerStyle);
                GUILayout.Label("All BRAA/distance calculations will be relative to the selected reference unit.", labelStyle);
                GUILayout.Label("Great for controlling other aircraft from an AWACS position!", labelStyle);
                GUILayout.Space(3);
                
                var radarSystem = ATCComponent.GetRadarSystem();
                if (radarSystem != null)
                {
                    Unit currentRef = radarSystem.ReferenceUnit;
                    bool awacsActive = currentRef != null;
                    
                    // Status box
                    if (awacsActive)
                    {
                        GUI.backgroundColor = new Color(0.2f, 0.5f, 0.3f, 1f);
                    }
                    else
                    {
                        GUI.backgroundColor = new Color(0.3f, 0.3f, 0.4f, 1f);
                    }
                    
                    GUILayout.BeginVertical(RowBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Status:", labelStyle, GUILayout.Width(60));
                    if (awacsActive)
                    {
                        GUI.color = Color.green;
                        GUILayout.Label($"ACTIVE - {currentRef.gameObject.name}", dataStyle);
                        GUI.color = oldColor;
                    }
                    else
                    {
                        GUI.color = Color.gray;
                        GUILayout.Label("OFF - Using Player Aircraft", dataStyle);
                        GUI.color = oldColor;
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    GUI.backgroundColor = Color.white;
                    
                    GUILayout.Space(3);
                    
                    GUILayout.BeginHorizontal();
                    if (awacsActive)
                    {
                        if (GUILayout.Button("Disable AWACS Mode", GUILayout.Height(24)))
                        {
                            radarSystem.ReferenceUnit = null;
                            Plugin.Log.LogInfo("[ATCWindow] AWACS mode disabled");
                        }
                    }
                    
                    if (selectedTarget1 != null && selectedTarget1.unit != currentRef)
                    {
                        string btnText = awacsActive ? $"Change to '{GetUnitDisplayName(selectedTarget1)}'" : $"Set '{GetUnitDisplayName(selectedTarget1)}' as Reference";
                        if (GUILayout.Button(btnText, GUILayout.Height(24)))
                        {
                            radarSystem.ReferenceUnit = selectedTarget1.unit;
                            Plugin.Log.LogInfo($"[ATCWindow] Reference point set to: {selectedTarget1.unit?.gameObject.name}");
                        }
                    }
                    else if (selectedTarget1 == null && !awacsActive)
                    {
                        GUI.enabled = false;
                        GUILayout.Button("Select a contact in the list to enable AWACS mode", GUILayout.Height(24));
                        GUI.enabled = true;
                    }
                    GUILayout.EndHorizontal();
                    
                    GUILayout.Space(3);
                    GUILayout.Label("TIP: Click a unit row in the radar list, then click 'Set Ref' or use the button above.", labelStyle);
                }
                
                GUILayout.Space(5);
                
                GUILayout.Label("Radar Settings:", headerStyle);
                Plugin.DataLinkOnly.Value = GUILayout.Toggle(Plugin.DataLinkOnly.Value, "DataLink Only (fair for MP, hides untracked enemies)");
                Plugin.StableSort.Value = GUILayout.Toggle(Plugin.StableSort.Value, "Stable Sort (sort by callsign, list doesn't jump)");
                
                GUILayout.BeginHorizontal();
                GUILayout.Label("Range (m):", labelStyle, GUILayout.Width(100));
                string rangeStr = GUILayout.TextField(Plugin.RadarRange.Value.ToString("F0"), GUILayout.Width(100));
                if (float.TryParse(rangeStr, out float newRange) && newRange > 0 && newRange != Plugin.RadarRange.Value)
                {
                    Plugin.RadarRange.Value = Mathf.Clamp(newRange, 1000f, 500000f);
                }
                GUILayout.EndHorizontal();
                
                GUILayout.BeginHorizontal();
                GUILayout.Label("Update Interval (s):", labelStyle, GUILayout.Width(100));
                string intervalStr = GUILayout.TextField(Plugin.RadarUpdateInterval.Value.ToString("F2"), GUILayout.Width(100));
                if (float.TryParse(intervalStr, out float newInterval) && newInterval > 0 && newInterval != Plugin.RadarUpdateInterval.Value)
                {
                    Plugin.RadarUpdateInterval.Value = Mathf.Clamp(newInterval, 0.05f, 5f);
                }
                GUILayout.EndHorizontal();
                
                GUILayout.Space(5);
                
                GUILayout.Label("Map Overlay Settings:", headerStyle);
                Plugin.EnableMapOverlay.Value = GUILayout.Toggle(Plugin.EnableMapOverlay.Value, "Enable Map Overlay");
                
                if (Plugin.EnableMapOverlay.Value)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(20);
                    GUILayout.BeginVertical();
                    Plugin.MapOverlayShowDistance.Value = GUILayout.Toggle(Plugin.MapOverlayShowDistance.Value, "Show Distance");
                    Plugin.MapOverlayShowAltitude.Value = GUILayout.Toggle(Plugin.MapOverlayShowAltitude.Value, "Show Altitude");
                    Plugin.MapOverlayShowSpeed.Value = GUILayout.Toggle(Plugin.MapOverlayShowSpeed.Value, "Show Speed");
                    Plugin.MapOverlayShowHeading.Value = GUILayout.Toggle(Plugin.MapOverlayShowHeading.Value, "Show Heading");
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                }
                
                GUILayout.Space(5);
                
                GUILayout.Label("Radar Coverage (Advanced):", headerStyle);
                Plugin.EnableRadarCoverage.Value = GUILayout.Toggle(Plugin.EnableRadarCoverage.Value, "Enable Radar Coverage");
                
                if (Plugin.EnableRadarCoverage.Value)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(20);
                    GUILayout.BeginVertical();
                    
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Max Elevation (m):", labelStyle, GUILayout.Width(120));
                    string maxElevStr = GUILayout.TextField(Plugin.RadarMaxElevation.Value.ToString("F0"), GUILayout.Width(100));
                    if (float.TryParse(maxElevStr, out float newMaxElev) && newMaxElev != Plugin.RadarMaxElevation.Value)
                    {
                        Plugin.RadarMaxElevation.Value = Mathf.Clamp(newMaxElev, 0f, 100000f);
                    }
                    GUILayout.EndHorizontal();
                    
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Min Elevation (m):", labelStyle, GUILayout.Width(120));
                    string minElevStr = GUILayout.TextField(Plugin.RadarMinElevation.Value.ToString("F0"), GUILayout.Width(100));
                    if (float.TryParse(minElevStr, out float newMinElev) && newMinElev != Plugin.RadarMinElevation.Value)
                    {
                        Plugin.RadarMinElevation.Value = Mathf.Clamp(newMinElev, -1000f, 100000f);
                    }
                    GUILayout.EndHorizontal();
                    
                    Plugin.UseTerrainMasking.Value = GUILayout.Toggle(Plugin.UseTerrainMasking.Value, "Use Terrain Masking");
                    
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                }
                
                GUILayout.Space(5);
                
                GUILayout.Label("Window Settings:", headerStyle);
                if (Plugin.WindowTransparent != null)
                {
                    Plugin.WindowTransparent.Value = GUILayout.Toggle(Plugin.WindowTransparent.Value, " Transparent background (see-through window)");
                }
                GUILayout.BeginHorizontal();
                GUILayout.Label("Width:", labelStyle, GUILayout.Width(60));
                string widthStr = GUILayout.TextField(Plugin.WindowWidth.Value.ToString("F0"), GUILayout.Width(80));
                if (float.TryParse(widthStr, out float newWidth) && newWidth > 0 && newWidth != Plugin.WindowWidth.Value)
                {
                    Plugin.WindowWidth.Value = Mathf.Clamp(newWidth, 420f, Screen.width);
                    windowRect.width = Plugin.WindowWidth.Value;
                }
                GUILayout.Label("Height:", labelStyle, GUILayout.Width(60));
                string heightStr = GUILayout.TextField(Plugin.WindowHeight.Value.ToString("F0"), GUILayout.Width(80));
                if (float.TryParse(heightStr, out float newHeight) && newHeight > 0 && newHeight != Plugin.WindowHeight.Value)
                {
                    Plugin.WindowHeight.Value = Mathf.Clamp(newHeight, 300f, Screen.height);
                    windowRect.height = Plugin.WindowHeight.Value;
                }
                GUILayout.EndHorizontal();
                
                GUILayout.BeginHorizontal();
                GUILayout.Label("Position X:", labelStyle, GUILayout.Width(80));
                string posXStr = GUILayout.TextField(Plugin.WindowPosX.Value.ToString("F0"), GUILayout.Width(80));
                if (float.TryParse(posXStr, out float newPosX) && newPosX != Plugin.WindowPosX.Value)
                {
                    Plugin.WindowPosX.Value = Mathf.Clamp(newPosX, 0f, Screen.width - windowRect.width);
                    windowRect.x = Plugin.WindowPosX.Value;
                }
                GUILayout.Label("Position Y:", labelStyle, GUILayout.Width(80));
                string posYStr = GUILayout.TextField(Plugin.WindowPosY.Value.ToString("F0"), GUILayout.Width(80));
                if (float.TryParse(posYStr, out float newPosY) && newPosY != Plugin.WindowPosY.Value)
                {
                    Plugin.WindowPosY.Value = Mathf.Clamp(newPosY, 0f, Screen.height - windowRect.height);
                    windowRect.y = Plugin.WindowPosY.Value;
                }
                GUILayout.EndHorizontal();
            }
            finally
            {
                GUILayout.EndScrollView();
            }
        }
        
        private string GetUnitDisplayName(TrackedUnit tracked)
        {
            if (tracked.unit == null) return "Unknown";
            
            string unitName = tracked.unit.gameObject.name;
            Aircraft? aircraft = tracked.unit as Aircraft;
            
            if (aircraft != null)
            {
                if (callsignCache.TryGetValue(aircraft, out string cachedCallsign))
                {
                    return cachedCallsign;
                }
                
                try
                {
                    if (aircraft.pilots != null && aircraft.pilots.Length > 0 && aircraft.pilots[0] != null)
                    {
                        var pilot = aircraft.pilots[0];
                        var playerField = pilot.GetType().GetField("player", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (playerField != null)
                        {
                            var player = playerField.GetValue(pilot);
                            if (player != null)
                            {
                                var nameField = player.GetType().GetField("PlayerName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (nameField != null)
                                {
                                    string playerName = nameField.GetValue(player) as string;
                                    if (!string.IsNullOrEmpty(playerName))
                                    {
                                        callsignCache[aircraft] = playerName;
                                        return playerName;
                                    }
                                }
                                
                                var nameProp = player.GetType().GetProperty("PlayerName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (nameProp != null)
                                {
                                    string playerName = nameProp.GetValue(player) as string;
                                    if (!string.IsNullOrEmpty(playerName))
                                    {
                                        callsignCache[aircraft] = playerName;
                                        return playerName;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (UnityEngine.Random.value < 0.01f)
                    {
                        Plugin.Log.LogWarning($"[ATCWindow] Error getting player name: {ex.Message}");
                    }
                }
            }
            
            return unitName;
        }

        private Aircraft GetPlayerAircraft()
        {
            try
            {
                var combatHUD = SceneSingleton<CombatHUD>.i;
                if (combatHUD != null && combatHUD.aircraft != null)
                {
                    return combatHUD.aircraft;
                }
                return null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ATCWindow] Error getting player aircraft: {ex.Message}");
                return null;
            }
        }

        public void Cleanup()
        {
            try
            {
                if (gameObject != null)
                {
                    Destroy(gameObject);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ATCWindow] Error destroying GameObject: {ex.Message}");
            }
        }
    }
}
