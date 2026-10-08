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

        // 射速
        public static ConfigEntry<bool> EnableFasterFire;
        public static ConfigEntry<float> FireRateMultiplier;
        public static ConfigEntry<bool> ForceBasicLaser;

        public static bool IsPaused = false;

        // ============================================================
        // 敌人锁定规则：名字关键词 + 最低血量上限 + 最低分数
        // ============================================================
        struct EnemyRule
        {
            public string Keyword;
            public float MinMaxHealth;
            public int MinPoint;
            public EnemyRule(string keyword, float minMaxHealth, int minPoint)
            {
                Keyword = keyword; MinMaxHealth = minMaxHealth; MinPoint = minPoint;
            }
        }

        static readonly EnemyRule[] EnemyRules = {
            new EnemyRule("browngrunt",        100f,    1),
            new EnemyRule("yellowspeeder",     60f,     1),
            new EnemyRule("introshipclaw",     100f,    1),
            new EnemyRule("cummy",             100f,    1),
            new EnemyRule("padparent",         100f,    1),
            new EnemyRule("growthparent",      750f,    0),     // 两种 GrowthParent（750 / 1750）都锁
            new EnemyRule("spine0",            80000f,  10),
            new EnemyRule("cockpitpivot",      0f,      3),
            new EnemyRule("intromed",          1000f,   3),
            new EnemyRule("missiler",          1900f,   0),
            new EnemyRule("missilel",          1750f,   0),
            new EnemyRule("introbossseparate", 2000f,   10),
            new EnemyRule("turret",            70f,     1),
            new EnemyRule("shipsmallwater",    70f,     1),
            new EnemyRule("tail",              500f,    1),
            new EnemyRule("segment1pivot",     300f,    3),
            new EnemyRule("barriertransform",  10000f,  10),
            new EnemyRule("armature",          14000f,  10),
            new EnemyRule("skull",             6000f,   20),
            new EnemyRule("cannon1animator",   500f,    0),
            new EnemyRule("mountainturret",    70f,     1),
            new EnemyRule("tunnelturret",      70f,     1),
            new EnemyRule("tunnelclaw",        100f,    1),
            new EnemyRule("moonaim",           100f,    1),
            new EnemyRule("moondrone",         120f,    1),
            new EnemyRule("rcover",            2200f,   5),
            new EnemyRule("lcover",            2200f,   5),
            new EnemyRule("targetpivot",       1500f,   1),
            new EnemyRule("eyepivot",          4000f,   10),
            new EnemyRule("head",              700f,    3),
        };

        // 优先级目标：只要它们还活着，强制优先锁
        static readonly string[] PriorityKeywords = {
            "rcover",
            "lcover",
            "barriertransform",
            "targetpivot",
            "growthparent",      // GrowthParent 优先
            "padparent",         // PadParent 优先
        };

        // 最低优先级目标：只有场上没有其他可锁目标时才打
        static readonly string[] LowestPriorityKeywords = {
            "spine0",            // Spine0 最后打
        };

        public static void Log(string msg)
        {
            if (Instance != null) Instance.Logger.LogInfo(msg);
        }

        private void Awake()
        {
            Instance = this;

            EnableAutoAim = Config.Bind("功能开关", "自动索敌", true, "是否自动将准星移动到最近的敌人身上。");
            EnableAutoFire = Config.Bind("功能开关", "自动射击", true, "是否自动开火。关闭后需要手动按键射击。");
            EnableHealthBars = Config.Bind("功能开关", "显示血量名字", true, "是否在敌人头顶显示血条和名字。");
            HealthBarRange = Config.Bind("功能开关", "血量显示距离", 120f, "超过这个距离（米）就不显示血条。");
            TargetRange = Config.Bind("功能开关", "索敌范围", 200f, "玩家周围多少米内的敌人会被锁定和自动射击。");

            EnableFasterFire = Config.Bind("射速", "启用射速调整", false, "是否修改武器射速（默认关闭）。");
            FireRateMultiplier = Config.Bind("射速", "射速倍率", 2.0f, "射速倍率。1.0 = 原版，2.0 = 两倍，3.0 = 三倍。");
            ForceBasicLaser = Config.Bind("射速", "强制使用基础激光", false, "只对精子枪（primaryType = -1）生效。把它的 primaryType 强制设为 0（basic laser）。");

            var harmony = new Harmony("com.yourname.starbreed.autocombat");

            TryPatch(harmony, typeof(AutoAimPatch));
            TryPatch(harmony, typeof(CaptureReticleBasePatch));
            TryPatch(harmony, typeof(AutoAimReticlePatch));
            TryPatch(harmony, typeof(ForceFireWasPressedThisFramePatch));
            TryPatch(harmony, typeof(ForceFireIsPressedPatch));
            TryPatch(harmony, typeof(ForceFireWasReleasedThisFramePatch));
            TryPatch(harmony, typeof(FasterFireRatePatch));

            Logger.LogInfo("Starbreed AutoCombat 已加载，版本 20260801-CC");
            Logger.LogInfo("快捷键：F2 自动开火 | F3 自动索敌 | F4 显示血量 | F5 暂停");
        }

        private static void TryPatch(Harmony harmony, System.Type patchType)
        {
            try
            {
                harmony.CreateClassProcessor(patchType).Patch();
                if (Instance != null) Instance.Logger.LogInfo($"[Patch] {patchType.Name} 注册成功");
            }
            catch (System.Exception ex)
            {
                if (Instance != null) Instance.Logger.LogError($"[Patch] {patchType.Name} 注册失败：{ex.Message}");
            }
        }

        private static readonly HashSet<EnemyStats> LoggedEnemies = new HashSet<EnemyStats>();

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f2Key.wasPressedThisFrame)
            {
                EnableAutoFire.Value = !EnableAutoFire.Value;
                Log($"[开关] 自动开火 = {EnableAutoFire.Value}");
            }
            if (keyboard.f3Key.wasPressedThisFrame)
            {
                EnableAutoAim.Value = !EnableAutoAim.Value;
                Log($"[开关] 自动索敌 = {EnableAutoAim.Value}");
            }
            if (keyboard.f4Key.wasPressedThisFrame)
            {
                EnableHealthBars.Value = !EnableHealthBars.Value;
                Log($"[开关] 显示血量 = {EnableHealthBars.Value}");
            }
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
                    Log($"[Enemy] {e.gameObject.name}, hp={e.health}, maxHp={e.maxHealth}, point={e.pointValue}");
                }
            }
        }

        public static bool IsKnownEnemy(EnemyStats e)
        {
            if (e == null) return false;
            string name = e.gameObject.name.ToLower();
            if (name.Contains("tailpivot")) return false;
            if (name.Contains("headpivot")) return false;

            foreach (var rule in EnemyRules)
            {
                if (name.Contains(rule.Keyword))
                {
                    if (e.maxHealth < rule.MinMaxHealth) return false;
                    if (e.pointValue < rule.MinPoint) return false;
                    return true;
                }
            }
            return false;
        }

        public static bool IsPriorityTarget(EnemyStats e)
        {
            if (e == null) return false;
            string name = e.gameObject.name.ToLower();
            foreach (var kw in PriorityKeywords)
                if (name.Contains(kw)) return true;
            return false;
        }

        public static bool IsLowestPriorityTarget(EnemyStats e)
        {
            if (e == null) return false;
            string name = e.gameObject.name.ToLower();
            foreach (var kw in LowestPriorityKeywords)
                if (name.Contains(kw)) return true;
            return false;
        }

        public static Transform GetNearestTarget(Vector3 fromPosition)
        {
            float maxSqr = TargetRange.Value * TargetRange.Value;

            // 第一层：优先目标（GrowthParent / PadParent / 护罩 / 屏障 / 脚眼睛）
            Transform best = FindNearest(fromPosition, maxSqr, priorityOnly: true, lowestOnly: false);
            if (best != null) return best;

            // 第二层：普通可锁目标，排除最低优先级（Spine0）
            best = FindNearest(fromPosition, maxSqr, priorityOnly: false, lowestOnly: false);
            if (best != null) return best;

            // 第三层：兜底，只剩最低优先级目标时才打（Spine0）
            best = FindNearest(fromPosition, maxSqr, priorityOnly: false, lowestOnly: true);
            return best;
        }

        private static Transform FindNearest(Vector3 fromPosition, float maxSqr, bool priorityOnly, bool lowestOnly)
        {
            Transform best = null;
            float bestDist = float.MaxValue;

            foreach (var e in ActiveEnemies)
            {
                if (e == null || e.dead || e.health <= 0f) continue;
                if (!e.gameObject.activeInHierarchy) continue;
                if (!IsKnownEnemy(e)) continue;

                bool isPriority = IsPriorityTarget(e);
                bool isLowest = IsLowestPriorityTarget(e);

                if (priorityOnly && !isPriority) continue;
                if (lowestOnly && !isLowest) continue;
                if (!priorityOnly && !lowestOnly && (isPriority || isLowest)) continue;

                float d = Vector3.SqrMagnitude(e.transform.position - fromPosition);
                if (d > maxSqr) continue;
                if (d < bestDist) { bestDist = d; best = e.transform; }
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
                float barWidth = 60f, barHeight = 6f;
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

    // 瞄准
    [HarmonyPatch(typeof(PlayerWeapon), "ShootLasers")]
    class AutoAimPatch
    {
        static void Postfix(PlayerWeapon __instance, GameObject laserType)
        {
            if (Plugin.IsPaused) return;
            if (!Plugin.EnableAutoAim.Value) return;
            if (__instance == null || __instance.cam == null) return;
            if (laserType == null) return;

            var target = Plugin.GetNearestTarget(__instance.cam.transform.position);
            if (target == null) return;

            Vector3 dir = target.position - laserType.transform.position;
            if (dir.sqrMagnitude > 0.01f)
                laserType.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }

    // 记录准星基准
    [HarmonyPatch(typeof(PlayerWeapon), "Init")]
    class CaptureReticleBasePatch
    {
        static void Postfix(PlayerWeapon __instance)
        {
            if (__instance != null && __instance.farReticle != null)
                AutoAimReticlePatch.SetBasePos(__instance, __instance.farReticle.localPosition);
        }
    }

    // ============================================================
    // 射速调整 + 只对精子枪强制 basic laser
    // ============================================================
    [HarmonyPatch(typeof(PlayerWeapon), "Update")]
    class FasterFireRatePatch
    {
        static readonly HashSet<PlayerWeapon> _loggedForce = new HashSet<PlayerWeapon>();

        static void Prefix(PlayerWeapon __instance)
        {
            if (__instance == null) return;

            // 强制 basic laser：只对精子枪（primaryType == -1）生效
            if (Plugin.ForceBasicLaser.Value && __instance.primaryType == -1)
            {
                __instance.primaryType = 0;
                if (_loggedForce.Add(__instance))
                    Plugin.Log("[FireRate] 精子枪 primaryType -1 -> 0 (basic laser)");
            }

            if (!Plugin.EnableFasterFire.Value) return;
            float mul = Plugin.FireRateMultiplier.Value;
            if (mul <= 1.0f) return;

            __instance.attackSpeed = 1f * mul;
            __instance.burstThresh = 0.3f / mul;
        }
    }

    // 自动开火
    [HarmonyPatch(typeof(InputAction), "WasPressedThisFrame")]
    class ForceFireWasPressedThisFramePatch
    {
        static void Postfix(InputAction __instance, ref bool __result)
        {
            if (Plugin.IsPaused) return;
            if (!Plugin.EnableAutoFire.Value) return;
            if (__instance == null) return;
            if (__instance.name != "Fire") return;
            if (Plugin.HasAutoFireTarget()) __result = true;
        }
    }

    [HarmonyPatch(typeof(InputAction), "IsPressed")]
    class ForceFireIsPressedPatch
    {
        static void Postfix(InputAction __instance, ref bool __result)
        {
            if (Plugin.IsPaused) return;
            if (!Plugin.EnableAutoFire.Value) return;
            if (__instance == null) return;
            if (__instance.name != "Fire") return;
            if (Plugin.HasAutoFireTarget()) __result = true;
        }
    }

    [HarmonyPatch(typeof(InputAction), "WasReleasedThisFrame")]
    class ForceFireWasReleasedThisFramePatch
    {
        static void Postfix(InputAction __instance, ref bool __result)
        {
            if (Plugin.IsPaused) return;
            if (!Plugin.EnableAutoFire.Value) return;
            if (__instance == null) return;
            if (__instance.name != "Fire") return;
            if (Plugin.HasAutoFireTarget()) __result = false;
        }
    }

    // 准星控制
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