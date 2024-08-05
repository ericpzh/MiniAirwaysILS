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

    [HarmonyPatch(typeof(PlaceableWaypoint), "Start", new Type[] {})]
    class PatchStart
    {
        static void Postfix(ref PlaceableWaypoint __instance)
        {
            if (__instance is WaypointAutoLanding)
            {
                // Basically __instance.SetFieldValue<State>("state", State.WaitingForSelectingRunway):
                __instance.GetType().GetField("state", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, 1 /* WaitingForSelectingRunway */);
            }
        }
    }

    [HarmonyPatch(typeof(PlaceableWaypoint), "Update", new Type[] {})]
    class PatchPlaceableWaypointUpdate
    {

        static Vector3 ClampToVP(Vector3 pos)
        {
            float num = Camera.main.orthographicSize - 1f;
            float num2 = Camera.main.orthographicSize / 9f * 16f - 1f;
            return new Vector3(Mathf.Clamp(pos.x, 0f - num2, num2), Mathf.Clamp(pos.y, 0f - num, num), pos.z);
        }

        static bool Prefix(ref PlaceableWaypoint __instance,  ref GameObject ____runwayTakeoffPointSelector, ref GameObject ___arrowContainer, ref bool ___canStartLockRotationProgress,
                           ref float ___placeDirectionTimer, ref float ____selectRunwayTimer, ref float ___longPressPlaceTime, ref Material ___mat, ref Runway ____selectedRunway,
                           ref GameObject ___PlaceProgress, ref TextMeshPro ___placeInstructionTMP)
        {
            int state = (int)(__instance.GetType()?.GetField("state", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(__instance));
            if (__instance is WaypointAutoLanding)
            {
                Vector3 _mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                Vector3 landingStartPoint;
                Vector3 landingEndPoint;

                if ((bool)___placeInstructionTMP)
                {
                    ___placeInstructionTMP.gameObject.SetActive(value: false);
                }
                ___arrowContainer.SetActive(value: false);

                if (state == 1)
                {
                    ___mat.SetFloat("_Steps", 0f);
                    Runway.ShowAllTakeoffPoints();
                    ____runwayTakeoffPointSelector?.SetActive(value: true);
                    __instance.transform.position = new Vector3(_mousePos.x, _mousePos.y, 1f);

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

                    ____selectedRunway = runway;
                    runway.ShowRunwayDirectionIndicator(landingStartPoint);

                    __instance.transform.position = landingStartPoint;
                    ___PlaceProgress.transform.position = landingStartPoint;

                    if (Input.GetMouseButton(0))
                    {
                        if (____selectRunwayTimer >= ___longPressPlaceTime)
                        {
                            // Basically __instance.SetFieldValue<State>("state", State.WaitingForLockingRotation):
                            runway.HideRunwayDirectionIndicator();
                            __instance.GetType().GetField("state", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, 3 /* WaitingForLockingRotation */);
                            Runway.HideAllTakeoffPoints();
                            UnityEngine.Object.Destroy(____runwayTakeoffPointSelector);
                        }
                        else
                        {
                            ____selectRunwayTimer += Time.unscaledDeltaTime;
                        }

                    } else {
                        __instance.ResetTimer();
                    }

                    if (___PlaceProgress.activeInHierarchy)
                    {
                        ___mat.SetFloat("_FillAmount", ____selectRunwayTimer / ___longPressPlaceTime);
                    }

                    return false;
                }
                else if (state == 3)
                {
                    Runway runway = Runway.GetClosestRwyPoint(_mousePos, out landingStartPoint, out landingEndPoint);

                    ___mat.SetFloat("_Steps", 0f);
                    ___PlaceProgress.SetActive(value: true);
                    if (!Input.GetMouseButton(0))
                    {
                        ___canStartLockRotationProgress = true;
                    }
                    if (Input.GetMouseButton(0) && ___canStartLockRotationProgress)
                    {
                        __instance.Invoke("AddToDirectionTimer", 0f);
                    }
                    else
                    {
                        __instance.ResetTimer();
                    }

                    // SetHeading()
                    ____selectedRunway.HideRunwayDirectionIndicator();
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

                    // Basically __instance.SetFieldValue<State>("state", State.WaitingForRepositioning):
                    __instance.GetType().GetField("state", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, 4 /*  WaitingForRepositioning */);
                    
                    if (___PlaceProgress.activeInHierarchy)
                    {
                        ___mat.SetFloat("_FillAmount", ___placeDirectionTimer / ___longPressPlaceTime);
                    }

                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PlaceableWaypoint), "OnMouseButtonDown", new Type[] {})]
    class PatchOnMouseButtonDown
    {
        static bool Prefix(ref PlaceableWaypoint __instance, ref bool ___CanMove)
        {
            if (__instance is WaypointAutoLanding)
            {
                ___CanMove = false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PlaceableWaypoint), "ProcessRedirect", new Type[] {})]
    class PatchProcessRedirect
    {
        static bool Prefix(ref PlaceableWaypoint __instance, ref float ____removeTimer, ref float ___longPressUnPlaceTime)
        {
            if (__instance is WaypointAutoLanding)
            {
                ____removeTimer = ___longPressUnPlaceTime;
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
