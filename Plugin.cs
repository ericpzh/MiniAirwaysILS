using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
// using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
// using UnityEngine.UIElements.UIR;

namespace MiniAirwaysILS
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {   
        private void Awake()
        {
            Log = Logger;

            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            
            SceneManager.sceneLoaded += OnSceneLoaded;
            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo($"Scene loaded: {scene.name}");
        }
        internal static ManualLogSource Log;
    }

    // Cancel add IsHovering when commanded by landing waypoint.
    [HarmonyPatch(typeof(WaypointAutoLanding), "OnLeavingFrom", new Type[] { typeof(Aircraft) })]
    class PatchOnLeavingFrom
    {
        static void Postfix(Aircraft aircraft)
        {
            aircraft.IsHovering = true;
        }
    }

    // Cancel IsHovering when landed.
    [HarmonyPatch(typeof(Aircraft), "FixedUpdate", new Type[] {})]
    class PatchFixedUpdate
    {
        static bool Prefix(ref Aircraft __instance)
        {
            if (__instance.IsHovering && __instance.state == Aircraft.State.TouchedDown)
            {
                float speed = __instance.speed;
                __instance.IsHovering = false;
                __instance.speed = speed;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PlaceableWaypoint), "GetInitialState", new Type[] {})]
    class PatchGetInitialState
    {
        enum State
        {
            None,
            WaitingForSelectingRunway,
            WaitingForPlacing,
            WaitingForLockingRotation,
            WaitingForRepositioning
        }

        static void Postfix(ref PlaceableWaypoint __instance, ref Enum __result, ref bool ____isPlacingInit)
        {
            if (__instance is WaypointAutoLanding)
            {
                __result = State.WaitingForSelectingRunway;
                // Use as status flag of done placing.
                ____isPlacingInit = true;
            }
        }
    }

    [HarmonyPatch(typeof(PlaceableWaypoint), "Update", new Type[] {})]
    class PatchPlaceableWaypointUpdate
    {
        enum State
        {
            None,
            WaitingForSelectingRunway,
            WaitingForPlacing,
            WaitingForLockingRotation,
            WaitingForRepositioning
        }

        static Vector3 ClampToVP(Vector3 pos)
        {
            float num = Camera.main.orthographicSize - 1f;
            float num2 = Camera.main.orthographicSize / 9f * 16f - 1f;
            return new Vector3(Mathf.Clamp(pos.x, 0f - num2, num2), Mathf.Clamp(pos.y, 0f - num, num), pos.z);
        }

        static bool Prefix(ref PlaceableWaypoint __instance, ref Enum ___state, ref GameObject ____runwayTakeoffPointSelector, ref Runway ____selectedRunway, ref GameObject ___PlaceProgress, ref TextMeshPro ___placeInstructionTMP, ref bool ____isPlacingInit)
        {
            if (__instance is WaypointAutoLanding && ____isPlacingInit)
            {
                if ((bool)___placeInstructionTMP)
                {
                    ___placeInstructionTMP.gameObject.SetActive(value: false);
                }

                ____runwayTakeoffPointSelector?.SetActive(value: true);
                Vector3 _mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                __instance.transform.position = new Vector3(_mousePos.x, _mousePos.y, 1f);
                Vector3 landingStartPoint;
                Vector3 landingEndPoint;
                Runway runway = Runway.GetClosestRwyPoint(_mousePos, out landingStartPoint, out landingEndPoint);
                if (!runway)
                {
                    if ((bool)____selectedRunway)
                    {
                        ____selectedRunway.HideRunwayDirectionIndicator();
                        ____selectedRunway = null;
                    }
                    return false;
                }
                if ((bool)____selectedRunway && ____selectedRunway != runway)
                {
                    ____selectedRunway.HideRunwayDirectionIndicator();
                }
                runway.ShowRunwayDirectionIndicator(landingStartPoint);
                __instance.transform.position = landingStartPoint;
                ___PlaceProgress.transform.position = landingStartPoint;
                ____selectedRunway = runway;

                if (Input.GetMouseButton(0))
                {
                    // SetHeading()
                    runway.HideRunwayDirectionIndicator();
                    Vector3 position = (landingStartPoint - landingEndPoint) * 3f + landingStartPoint;
                    __instance.transform.position = ClampToVP(new Vector3(position.x, position.y, 1f));
                    __instance.SetFieldValue<Runway>("_targetRunway", runway);
                    __instance.SetFieldValue<Vector3>("_landingStartPoint", landingStartPoint);
                    UnityEngine.Object.Destroy(____runwayTakeoffPointSelector);

                    // LockPointerRotation()
                    if (__instance.autoResume)
                    {
                        TimeManager.Instance.Resume();
                    }
                    TimeManager.Instance.AllowManualTimeControl();
                    ___PlaceProgress.SetActive(value: false);

                    WaypointPropsManager.Instance.AddPlaceableWaypoint(__instance);
                    WaypointPropsManager.Instance.HasPlacingProps = false;
                    UpgradeManager.Instance.ExtCompleteUpgrade();

                    __instance.Invoke("RenderLineIndicator", 0);
                    ___state = State.None;
                    ____isPlacingInit = false;
                }
                return false;
            }
            return true;
        }
    }

    
    public static class ReflectionExtensions
    {
        public static T GetFieldValue<T>(this object obj, string name)
        {
            // Set the flags so that private and public fields from instances will be found
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var field = obj.GetType().GetField(name, bindingFlags);
            return (T)field?.GetValue(obj);
        }

        public static void SetFieldValue<T>(this object obj, string name, T value)
        {
            // Set the flags so that private and public fields from instances will be found
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var field = obj.GetType().GetField(name, bindingFlags);
            field.SetValue(obj, value);
        }
    }
}
