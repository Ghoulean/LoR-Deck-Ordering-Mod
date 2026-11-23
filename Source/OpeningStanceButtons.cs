using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace DeckOrdering
{
    [HarmonyPatch(typeof(UIEquipDeckCardList), nameof(UIEquipDeckCardList.SetData))]
    internal static class AttachOpeningStanceButtons
    {
        private static void Postfix(UIEquipDeckCardList __instance)
        {
            try
            {
                OpeningStanceButtons buttons = OpeningStanceButtons.Ensure(__instance);
                if (buttons != null)
                    buttons.Bind(__instance, __instance.currentunit?.bookItem);
            }
            catch (Exception e)
            {
                Debug.Log(Initializer.LogPrefix + "Opening stance buttons failed: " + e);
            }
        }
    }

    internal class OpeningStanceButtons : MonoBehaviour
    {
        private static readonly string[] Labels = { "Random", "Slash", "Pierce", "Blunt", "Guard" };

        private const float ButtonWidth = 90f;
        private const float ButtonHeight = 34f;
        private const float Gap = 6f;
        private const float FontSize = 18f;
        private static readonly Vector2 RowOffset = new Vector2(0f, 8f);

        private static readonly Color Normal = new Color(0.15f, 0.12f, 0.1f, 0.85f);
        private static readonly Color Selected = new Color(0.55f, 0.3f, 0.75f, 0.95f);

        private static readonly AccessTools.FieldRef<UIEquipDeckCardList, GameObject> MultiDeckLayout =
            AccessTools.FieldRefAccess<UIEquipDeckCardList, GameObject>("multiDeckLayout");

        private readonly List<Image> backgrounds = new List<Image>();
        private BookModel book;
        private UIEquipDeckCardList list;

        internal static OpeningStanceButtons Ensure(UIEquipDeckCardList list)
        {
            GameObject layout = MultiDeckLayout(list);
            if (layout == null)
                return null;
            OpeningStanceButtons existing = layout.GetComponentInChildren<OpeningStanceButtons>(true);
            if (existing != null)
                return existing;

            TextMeshProUGUI sampleText = list.GetComponentInChildren<TextMeshProUGUI>(true);

            GameObject row = new GameObject("DeckOrdering_OpeningStance", typeof(RectTransform));
            row.transform.SetParent(layout.transform, false);
            RectTransform rt = (RectTransform)row.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = RowOffset;
            rt.sizeDelta = new Vector2(Labels.Length * (ButtonWidth + Gap), ButtonHeight);

            OpeningStanceButtons buttons = row.AddComponent<OpeningStanceButtons>();
            for (int i = 0; i < Labels.Length; i++)
                buttons.AddButton(i, sampleText != null ? sampleText.font : null);
            return buttons;
        }

        private void AddButton(int index, TMP_FontAsset font)
        {
            GameObject go = new GameObject(Labels[index], typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(index * (ButtonWidth + Gap), 0f);
            rt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);

            Image image = go.GetComponent<Image>();
            image.color = Normal;
            backgrounds.Add(image);

            int? stance = index == 0 ? (int?)null : index - 1;
            go.GetComponent<Button>().onClick.AddListener(() => Select(stance));

            GameObject labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            RectTransform labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
                label.font = font;
            label.text = Labels[index];
            label.fontSize = FontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
        }

        internal void Bind(UIEquipDeckCardList owner, BookModel newBook)
        {
            list = owner;
            book = newBook;
            bool show = PurpleTear.IsPurpleTear(book);
            gameObject.SetActive(show);
            if (show)
                Recolor();
        }

        private void Select(int? stance)
        {
            if (book == null)
                return;
            OpeningStanceStore.Set(book, stance);
            if (list != null)
                list.changed = true;
            UISoundManager.instance.PlayEffectSound(UISoundType.Card_Apply);
            Recolor();
        }

        private void Recolor()
        {
            int? stance = OpeningStanceStore.Get(book);
            int selected = stance.HasValue ? stance.Value + 1 : 0;
            for (int i = 0; i < backgrounds.Count; i++)
                backgrounds[i].color = i == selected ? Selected : Normal;
        }
    }
}
