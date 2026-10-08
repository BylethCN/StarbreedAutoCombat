using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StarbreedAutoCombat
{
    [BepInPlugin("com.yourname.starbreed.autocombat", "Starbreed AutoCombat", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        public static readonly List<EnemyStats> ActiveEnemies = new List<EnemyStats>();

        public static ConfigEntry<bool> EnableAutoAim;
        public static ConfigEntry<bool> EnableAutoFire;
        public static ConfigEntry<bool> EnableHealthBars;
        public static ConfigEntry<float> HealthBarRange;
        public static ConfigEntry<float> TargetRange;

        public static bool IsPaused = false;

        // ============================================================
        // 可锁定敌人白名单
        // ============================================================
        static readonly string[] EnemyKeywords = {
            "browngrunt",        // 小怪：棕地精
            "yellowspeeder",     // 小怪：黄速艇
            "introshipclaw",     // 小怪：序章爪
            "cockpitpivot",      // 未知（暂时保留）
            "intromed",          // 中BOSS：IntroMed
            "missiler",          // 猪仔：右导弹舱（不打进不了下一阶段）
            "missilel",          // 猪仔：左导弹舱（不打进不了下一阶段）
            "introbossseparate", // 猪仔：驾驶舱
            "turret",            // 小怪：炮台
            "shipsmallwater",    // 小怪：水面小船
            "tail",              // 蛇形怪1：尾巴（唯一可打部位）
            "segment1pivot",     // 蛇形怪2：可打部位（point=3）
            "barriertransform",  // 诺娃：屏障（point=10）
            "armature",          // 诺娃：骨架（point=10）
            "skull",             // 军刀：头（point=20）
            "cannon1animator",   // 军刀：炮台（先打掉才能打头）
            "mountainturret",    // 小怪：山上的炮台
            "tunnelturret",      // 小怪：隧道炮台
            "tunnelclaw",        // 小怪：隧道爪
            "moonaim",           // 小怪：MoonAim（point=1）
            "moondrone",         // 小怪：MoonDrone（point=1）
            "rcover",            // 威宝：右护罩（hp=2200, point=5，先打）
            "lcover",            // 威宝：左护罩（hp=2200, point=5，先打）
        };

        // 威宝护罩关键词：只要它们还活着，强制优先锁
        static readonly string[] PriorityKeywords = {
            "rcover",
            "lcover",
        };

        public static void Log(string msg)
        {
            if (Instance != null)
                Instance.Logger.LogInfo(msg);
        }

        private void Awake()
        {
            Instance = this;

            EnableAutoAim = Config.Bind("功能开关", "自动索敌", true, "是否自动将准星移动到最近的敌人身上。");
            EnableAutoFire = Config.Bind("功能开关", "自动射击", true, "是否自动开火。关闭后需要手动按键射击。");
            EnableHealthBars = Config.Bind("功能开关", "显示血量名字", true, "是否在敌人头顶显示血条和名字。");
            HealthBarRange = Config.Bind("功能开关", "血量显示距离", 120f, "超过这个距离（米）就不显示血条。");
            TargetRange = Config.Bind("功能开关", "索敌范围", 200f, "玩家周围多少米内的敌人会被锁定和自动射击。");

            var harmony = new Harmony("com.yourname.starbreed.autocombat");

            TryPatch(harmony, typeof(AutoAimPatch));
            TryPatch(harmony, typeof(CaptureReticleBasePatch));
            TryPatch(harmony, typeof(AutoAimReticlePatch));
            TryPatch(harmony, typeof(ForceFireWasPressedThisFramePatch));
            TryPatch(harmony, typeof(ForceFireIsPressedPatch));
            TryPatch(harmony, typeof(ForceFireWasReleasedThisFramePatch));

            Logger.LogInfo("Starbreed AutoCombat 已加载，版本 20260801-BK");
            Logger.LogInfo("快捷键：F2 自动开火 | F3 自动索敌 | F4 显示血量 | F5 暂停");
        }

        private static void TryPatch(Harmony harmony, System.Type patchType)
        {
            try
            {
                harmony.CreateClassProcessor(patchType).Patch();
                if (Instance != null)
                    Instance.Logger.LogInfo($"[Patch] {patchType.Name} 注册成功");
            }
            catch (System.Exception ex)
            {
                if (Instance != null)
                    Instance.Logger.LogError($"[Patch] {patchType.Name} 注册失败：{ex.Message}");
            }
        }

        private static readonly HashSet<EnemyStats> LoggedEnemies = new HashSet<EnemyStats>();

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // F2：切换自动开火
            if (keyboard.f2Key.wasPressedThisFrame)
            {
                EnableAutoFire.Value = !EnableAutoFire.Value;
                Log($"[开关] 自动开火 = {EnableAutoFire.Value}");
            }

            // F3：切换自动索敌
            if (keyboard.f3Key.wasPressedThisFrame)
            {
                EnableAutoAim.Value = !EnableAutoAim.Value;
                Log($"[开关] 自动索敌 = {EnableAutoAim.Value}");
            }

            // F4：切换显示血量
            if (keyboard.f4Key.wasPressedThisFrame)
            {
                EnableHealthBars.Value = !EnableHealthBars.Value;
                Log($"[开关] 显示血量 = {EnableHealthBars.Value}");
            }

            // F5：暂停游戏
            if (keyboard.f5Key.wasPressedThisFrame)
            {
                IsPaused = !IsPaused;
                Time.timeScale = IsPaused ? 0f : 1f;
                AudioListener.pause = IsPaused;
                Log($"[Pause] {(IsPaused ? "游戏已暂停" : "游戏已恢复")}");
            }

            if (IsPaused) return;

            ActiveEnemies.Clear();
            var all = GameObject.FindObjectsOfType<EnemyStats>();
            foreach (var e in all)
            {
                if (e == null || !e.isActiveAndEnabled || e.dead || e.health <= 0f) continue;
                ActiveEnemies.Add(e);

                if (!LoggedEnemies.Contains(e))
                {
                    LoggedEnemies.Add(e);
                    Log($"[Enemy] {e.gameObject.name}, hp={e.health}, point={e.pointValue}");
                }
            }
        }

        public static bool IsKnownEnemy(EnemyStats e)
        {
            if (e == null) return false;
            string name = e.gameObject.name.ToLower();

            // 排除不可攻击的关节
            if (name.Contains("tailpivot")) return false;
            if (name.Contains("headpivot")) return false;

            // 特判 "head"：
            //   - 蛇形怪1的 Head 是 point=1（打不了）→ 不锁
            //   - Lapras 的 Head 是 point=5（能打）→ 锁
            //   - 威宝的 Head 是 point=10（护罩破后能打）→ 锁
            if (name.Contains("head"))
            {
                return e.pointValue >= 5;
            }

            foreach (var kw in EnemyKeywords)
            {
                if (name.Contains(kw)) return true;
            }
            return false;
        }

        // 是否是优先级目标（威宝护罩）
        public static bool IsPriorityTarget(EnemyStats e)
        {
            if (e == null) return false;
            string name = e.gameObject.name.ToLower();
            foreach (var kw in PriorityKeywords)
            {
                if (name.Contains(kw)) return true;
            }
            return false;
        }

        public static Transform GetNearestTarget(Vector3 fromPosition)
        {
            // 第一步：先找优先级目标（威宝护罩）
            Transform best = null;
            float bestDist = float.MaxValue;
            float maxSqr = TargetRange.Value * TargetRange.Value;

            foreach (var e in ActiveEnemies)
            {
                if (e == null || e.dead || e.health <= 0f) continue;
                if (!e.gameObject.activeInHierarchy) continue;
                if (!IsKnownEnemy(e)) continue;
                if (!IsPriorityTarget(e)) continue;

                float d = Vector3.SqrMagnitude(e.transform.position - fromPosition);
                if (d > maxSqr) continue;

                if (d < bestDist)
                {
                    bestDist = d;
                    best = e.transform;
                }
            }

            if (best != null) return best;

            // 第二步：没有优先级目标时，找最近的可锁目标
            bestDist = float.MaxValue;
            foreach (var e in ActiveEnemies)
            {
                if (e == null || e.dead || e.health <= 0f) continue;
                if (!e.gameObject.activeInHierarchy) continue;
                if (!IsKnownEnemy(e)) continue;

                float d = Vector3.SqrMagnitude(e.transform.position - fromPosition);
                if (d > maxSqr) continue;

                if (d < bestDist)
                {
                    bestDist = d;
                    best = e.transform;
                }
            }

            return best;
        }

        private static PlayerWeapon _cachedWeapon;
        public static PlayerWeapon GetPlayerWeapon()
        {
            if (_cachedWeapon == null)
                _cachedWeapon = GameObject.FindObjectOfType<PlayerWeapon>();
            return _cachedWeapon;
        }

        public static bool HasAutoFireTarget()
        {
            if (IsPaused) return false;
            if (!EnableAutoFire.Value) return false;
            var weapon = GetPlayerWeapon();
            if (weapon == null || weapon.cam == null) return false;
            return GetNearestTarget(weapon.cam.transform.position) != null;
        }

        private static Texture2D _whiteTex;
        private static GUIStyle _labelStyle;

        private void OnGUI()
        {
            if (!EnableHealthBars.Value) return;
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 playerPos = cam.transform.position;
            Vector3 playerForward = cam.transform.forward;

            foreach (var e in ActiveEnemies)
            {
                if (e == null || e.dead || e.health <= 0f) continue;
                if (!e.gameObject.activeInHierarchy) continue;
                if (!IsKnownEnemy(e)) continue;

                Vector3 toEnemy = e.transform.position - playerPos;
                float dist = toEnemy.magnitude;
                if (dist > HealthBarRange.Value) continue;

                float dot = Vector3.Dot(playerForward.normalized, toEnemy.normalized);
                if (dot < 0f) continue;

                Vector3 screenPos = cam.WorldToScreenPoint(e.transform.position);
                if (screenPos.z <= 0) continue;

                float x = screenPos.x;
                float y = Screen.height - screenPos.y;

                float barWidth = 60f;
                float barHeight = 6f;
                float barX = x - barWidth / 2f;
                float barY = y - 25f;

                DrawRect(new Rect(barX - 1, barY - 1, barWidth + 2, barHeight + 2), Color.black);
                float pct = Mathf.Clamp01(e.health / Mathf.Max(1f, e.maxHealth));
                Color hpColor = Color.Lerp(Color.red, Color.green, pct);
                DrawRect(new Rect(barX, barY, barWidth * pct, barHeight), hpColor);

                if (_labelStyle == null)
                {
                    _labelStyle = new GUIStyle(GUI.skin.label);
                    _labelStyle.alignment = TextAnchor.MiddleCenter;
                    _labelStyle.fontSize = 12;
                }
                _labelStyle.normal.textColor = Color.white;

                string cleanName = e.gameObject.name;
                int parenIdx = cleanName.IndexOf(" (");
                if (parenIdx > 0) cleanName = cleanName.Substring(0, parenIdx);

                string label = $"{cleanName}\n{e.health:F0}/{e.maxHealth:F0}";
                GUI.Label(new Rect(x - 60, barY - 30, 120, 30), label, _labelStyle);
            }
        }

        private static void DrawRect(Rect r, Color c)
        {
            if (_whiteTex == null)
            {
                _whiteTex = new Texture2D(1, 1);
                _whiteTex.SetPixel(0, 0, Color.white);
                _whiteTex.Apply();
            }
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _whiteTex);
            GUI.color = old;
        }
    }

    [HarmonyPatch(typeof(PlayerWeapon), "ShootLasers")]
    class AutoAimPatch
    {
        static void Postfix(PlayerWeapon __instance, GameObject laserType)
        {
            if (Plugin.IsPaused) return;
            if (!Plugin.EnableAutoAim.Value) return; // 自动索敌关闭时完全不动
            if (__instance == null || __instance.cam == null) return;
            if (laserType == null) return;

            var target = Plugin.GetNearestTarget(__instance.cam.transform.position);
            if (target == null) return;

            Vector3 dir = target.position - laserType.transform.position;
            if (dir.sqrMagnitude > 0.01f)
            {
                laserType.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerWeapon), "Init")]
    class CaptureReticleBasePatch
    {
        static void Postfix(PlayerWeapon __instance)
        {
            if (__instance != null && __instance.farReticle != null)
            {
                AutoAimReticlePatch.SetBasePos(__instance, __instance.farReticle.localPosition);
            }
        }
    }

    [HarmonyPatch(typeof(InputAction), "WasPressedThisFrame")]
    class ForceFireWasPressedThisFramePatch
    {
        static void Postfix(InputAction __instance, ref bool __result)
        {
            if (Plugin.IsPaused) return;
            if (!Plugin.EnableAutoFire.Value) return; // 自动开火关闭时完全不动
            if (__instance == null) return;
            if (__instance.name != "Fire") return;

            if (Plugin.HasAutoFireTarget())
                __result = true;
        }
    }

    [HarmonyPatch(typeof(InputControlExtensions), "IsPressed")]
    class ForceFireIsPressedPatch
    {
        static void Postfix(InputControl control, ref bool __result)
        {
            if (Plugin.IsPaused) return;
            if (!Plugin.EnableAutoFire.Value) return; // 自动开火关闭时完全不动
            if (control == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;
            if (control != mouse.leftButton) return;

            if (Plugin.HasAutoFireTarget())
                __result = true;
        }
    }

    [HarmonyPatch(typeof(InputAction), "WasReleasedThisFrame")]
    class ForceFireWasReleasedThisFramePatch
    {
        static void Postfix(InputAction __instance, ref bool __result)
        {
            if (Plugin.IsPaused) return;
            if (!Plugin.EnableAutoFire.Value) return; // 自动开火关闭时完全不动
            if (__instance == null) return;
            if (__instance.name != "Fire") return;

            if (Plugin.HasAutoFireTarget())
                __result = false;
        }
    }

    [HarmonyPatch(typeof(PlayerWeapon), "Update")]
    class AutoAimReticlePatch
    {
        static readonly Dictionary<PlayerWeapon, Vector3> reticleBasePos = new Dictionary<PlayerWeapon, Vector3>();

        public static void SetBasePos(PlayerWeapon w, Vector3 pos)
        {
            reticleBasePos[w] = pos;
        }

        static void Postfix(PlayerWeapon __instance)
        {
            if (Plugin.IsPaused) return;
            if (__instance == null || __instance.cam == null || __instance.farReticle == null) return;

            // 自动索敌关闭时：把准星恢复到游戏初始基准位置，交还原版逻辑
            if (!Plugin.EnableAutoAim.Value)
            {
                if (!reticleBasePos.ContainsKey(__instance))
                    reticleBasePos[__instance] = __instance.farReticle.localPosition;
                __instance.farReticle.localPosition = reticleBasePos[__instance];
                return;
            }

            var target = Plugin.GetNearestTarget(__instance.cam.transform.position);

            if (target == null || target.gameObject == null)
            {
                if (!reticleBasePos.ContainsKey(__instance))
                    reticleBasePos[__instance] = __instance.farReticle.localPosition;
                __instance.farReticle.localPosition = reticleBasePos[__instance];
                return;
            }

            __instance.farReticle.position = target.position;
        }
    }
}