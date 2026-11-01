using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Bulbul;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ChillCostumeSwitcher
{
    /// <summary>
    /// 《Chill with You : Lo-Fi Story》换装面板（UGUI 版）。
    ///
    /// 坑 1：游戏启动时会清掉所有根物体（含 DontDestroyOnLoad 里的），
    ///       所以面板不靠插件自己的 MonoBehaviour 活着，而是用 Harmony 钩
    ///       RoomGameManager.Update 做"发现没了就重建"。
    /// 坑 2：这游戏不合成 IMGUI，必须用 UGUI Canvas。
    /// </summary>
    [BepInPlugin(Guid, "Costume Switcher", "2.0.0")]
    public class CostumeSwitcherPlugin : BaseUnityPlugin
    {
        public const string Guid = "local.chillwithyou.costumeswitcher";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            Settings.Bind(Config);

            try
            {
                new Harmony(Guid).PatchAll(typeof(RoomUpdatePatch));
            }
            catch (Exception e)
            {
                Log.LogError("Harmony patch failed: " + e);
            }

            CostumePanel.Create();
        }
    }

    [HarmonyPatch(typeof(RoomGameManager), "Update")]
    public static class RoomUpdatePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            CostumePanel.Ensure();
        }
    }

    internal static class Settings
    {
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<bool> NumberKeys;

        internal static void Bind(ConfigFile cfg)
        {
            ToggleKey = cfg.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F8),
                "显示 / 隐藏换装面板");
            NumberKeys = cfg.Bind("General", "NumberKeys", true,
                "数字键 1-6 直接换装");
        }
    }

    public class CostumePanel : MonoBehaviour
    {
        private struct Outfit
        {
            public readonly string Label;
            public readonly CostumeChangeService.CostumeSkinType Skin;

            public Outfit(string label, CostumeChangeService.CostumeSkinType skin)
            {
                Label = label;
                Skin = skin;
            }
        }

        private static readonly Outfit[] Outfits =
        {
            new Outfit("默认 Default",    CostumeChangeService.CostumeSkinType.Default_1),
            new Outfit("Polo 1",          CostumeChangeService.CostumeSkinType.Polo_1),
            new Outfit("Polo 2",          CostumeChangeService.CostumeSkinType.Polo_2),
            new Outfit("Tee 1",           CostumeChangeService.CostumeSkinType.Tee_1),
            new Outfit("Tee 2",           CostumeChangeService.CostumeSkinType.Tee_2),
            new Outfit("背带裤 Overalls", CostumeChangeService.CostumeSkinType.Overalls_1),
        };

        private static readonly FieldInfo CurrentSkinField =
            typeof(CostumeChangeService).GetField("_currentSkinType",
                BindingFlags.Instance | BindingFlags.NonPublic);

        internal static CostumePanel Instance;

        private GameObject _panel;
        private Text _status;
        private Image[] _buttonBg;
        private Text[] _buttonText;
        private CostumeChangeService _service;
        private float _nextSearch;
        private int _current = -1;

        private static readonly Color Accent = new Color(0.98f, 0.72f, 0.76f, 1f);
        private static readonly Color Idle = new Color(0.20f, 0.20f, 0.24f, 1f);
        private static readonly Color ActiveBg = new Color(0.45f, 0.28f, 0.32f, 1f);

        internal static void Create()
        {
            try
            {
                var go = new GameObject("CostumeSwitcherUI");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<CostumePanel>();
            }
            catch (Exception e)
            {
                if (CostumeSwitcherPlugin.Log != null)
                {
                    CostumeSwitcherPlugin.Log.LogError("Create failed: " + e);
                }
            }
        }

        internal static void Ensure()
        {
            if (Instance == null)
            {
                Create();
            }
        }

        private void Awake()
        {
            Instance = this;
            BuildUi();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private static Font _font;

        private static Font GetFont()
        {
            if (_font != null)
            {
                return _font;
            }

            try
            {
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Segoe UI", "Arial" }, 16);
            }
            catch
            {
                _font = null;
            }

            if (_font == null)
            {
                try
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                catch
                {
                    _font = null;
                }
            }

            return _font;
        }

        private static Text NewText(Transform parent, string name, string content, int size, Color color,
            TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = GetFont();
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static void Place(GameObject go, float x, float y, float w, float h)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("CostumeSwitcherCanvas");
            UnityEngine.Object.DontDestroyOnLoad(canvasGo);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            int rows = Outfits.Length;
            const float w = 240f;
            float h = 44f + 34f + rows * 40f + 30f;

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(canvasGo.transform, false);
            var prt = (RectTransform)_panel.transform;
            prt.anchorMin = new Vector2(1f, 0.5f);
            prt.anchorMax = new Vector2(1f, 0.5f);
            prt.pivot = new Vector2(1f, 0.5f);
            prt.anchoredPosition = new Vector2(-24f, 0f);
            prt.sizeDelta = new Vector2(w, h);
            _panel.GetComponent<Image>().color = new Color(0.07f, 0.07f, 0.10f, 0.93f);

            var title = NewText(_panel.transform, "Title", "换装 Outfit", 16, Accent, TextAnchor.MiddleLeft);
            Place(title.gameObject, 14f, 12f, 150f, 24f);

            var closeBtn = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeBtn.transform.SetParent(_panel.transform, false);
            Place(closeBtn, w - 34f, 10f, 24f, 24f);
            closeBtn.GetComponent<Image>().color = Idle;
            closeBtn.GetComponent<Button>().onClick.AddListener(() => _panel.SetActive(false));
            var closeTxt = NewText(closeBtn.transform, "CloseX", "×", 16, Color.white, TextAnchor.MiddleCenter);
            var crt = (RectTransform)closeTxt.transform;
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            _buttonBg = new Image[rows];
            _buttonText = new Text[rows];

            for (int i = 0; i < rows; i++)
            {
                int idx = i;
                float y = 40f + i * 40f;

                var btnGo = new GameObject("Btn" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                btnGo.transform.SetParent(_panel.transform, false);
                Place(btnGo, 14f, y, w - 28f, 34f);

                _buttonBg[i] = btnGo.GetComponent<Image>();
                _buttonBg[i].color = Idle;

                var txt = NewText(btnGo.transform, "Label", (i + 1) + "  " + Outfits[i].Label, 15,
                    Color.white, TextAnchor.MiddleLeft);
                var trt = (RectTransform)txt.transform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(12f, 0f);
                trt.offsetMax = new Vector2(-8f, 0f);
                _buttonText[i] = txt;

                var b = btnGo.GetComponent<Button>();
                b.targetGraphic = _buttonBg[i];
                b.onClick.AddListener(() => { Apply(idx).Forget(); });
            }

            _status = NewText(_panel.transform, "Status", "F8 隐藏面板", 12,
                new Color(0.72f, 0.72f, 0.76f, 1f), TextAnchor.MiddleLeft);
            Place(_status.gameObject, 14f, 40f + rows * 40f + 6f, w - 28f, 22f);
        }

        private void Update()
        {
            try
            {
                if (Settings.ToggleKey != null && Settings.ToggleKey.Value.IsDown() && _panel != null)
                {
                    _panel.SetActive(!_panel.activeSelf);
                }

                if (Settings.NumberKeys != null && Settings.NumberKeys.Value)
                {
                    for (int i = 0; i < Outfits.Length && i < 9; i++)
                    {
                        if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                        {
                            Apply(i).Forget();
                        }
                    }
                }
            }
            catch
            {
                // 游戏若禁用了旧版 Input，别让 Update 每帧抛
            }

            if (_service == null && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 2f;
                _service = FindService();
                if (_service != null)
                {
                    RefreshCurrent();
                }
            }
        }

        private static CostumeChangeService FindService()
        {
            try
            {
                return FindObjectOfType<CostumeChangeService>();
            }
            catch
            {
                return null;
            }
        }

        private void RefreshCurrent()
        {
            if (_service == null || CurrentSkinField == null)
            {
                return;
            }

            try
            {
                var skin = (CostumeChangeService.CostumeSkinType)CurrentSkinField.GetValue(_service);
                _current = IndexOf(skin);
                Paint();
            }
            catch
            {
            }
        }

        private static int IndexOf(CostumeChangeService.CostumeSkinType skin)
        {
            for (int i = 0; i < Outfits.Length; i++)
            {
                if (Outfits[i].Skin == skin)
                {
                    return i;
                }
            }

            return -1;
        }

        private void Paint()
        {
            if (_buttonBg == null)
            {
                return;
            }

            for (int i = 0; i < _buttonBg.Length; i++)
            {
                if (_buttonBg[i] == null)
                {
                    continue;
                }

                _buttonBg[i].color = i == _current ? ActiveBg : Idle;
                _buttonText[i].color = i == _current ? Accent : Color.white;
            }
        }

        private async UniTaskVoid Apply(int index)
        {
            if (index < 0 || index >= Outfits.Length)
            {
                return;
            }

            if (_service == null)
            {
                _service = FindService();
                if (_service == null)
                {
                    SetStatus("还没进房间，找不到角色服务");
                    return;
                }
            }

            if (_service.IsChangingCostume)
            {
                SetStatus("正在换装，稍等一下…");
                return;
            }

            var outfit = Outfits[index];
            SetStatus("切换中… " + outfit.Label);

            try
            {
                await _service.ChangeCostume(outfit.Skin);
            }
            catch (Exception e)
            {
                CostumeSwitcherPlugin.Log.LogError("ChangeCostume failed: " + e);
                SetStatus("换装失败，详见 BepInEx 日志");
                return;
            }

            _current = index;
            Paint();
            SetStatus("当前：" + outfit.Label);
        }

        private void SetStatus(string s)
        {
            if (_status != null)
            {
                _status.text = s;
            }
        }
    }
}
