using System;
using System.Collections.Generic;
using System.Linq;
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
                {
                    buttons.Bind(__instance, __instance.currentunit?.bookItem);
                }
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

        private static readonly Func<UIEquipDeckCardList, GameObject> MultiDeckLayout =
            Reflect.FieldGetter<UIEquipDeckCardList, GameObject>("multiDeckLayout");

        private IReadOnlyList<Image> backgrounds = new Image[0];
        private BookModel book;
        private UIEquipDeckCardList list;

        internal static OpeningStanceButtons Ensure(UIEquipDeckCardList list)
        {
            GameObject layout = MultiDeckLayout(list);
            if (layout == null)
            {
                return null;
            }

            OpeningStanceButtons existing = layout.GetComponentInChildren<OpeningStanceButtons>(true);
            return existing != null ? existing : Create(list, layout);
        }

        private static OpeningStanceButtons Create(UIEquipDeckCardList list, GameObject layout)
        {
            TextMeshProUGUI sampleText = list.GetComponentInChildren<TextMeshProUGUI>(true);
            TMP_FontAsset font = sampleText != null ? sampleText.font : null;

            GameObject row = new GameObject("DeckOrdering_OpeningStance", typeof(RectTransform));
            row.transform.SetParent(layout.transform, false);
            Place((RectTransform)row.transform, new Vector2(0f, 1f), new Vector2(0f, 0f), RowOffset,
                new Vector2(Labels.Length * (ButtonWidth + Gap), ButtonHeight));

            OpeningStanceButtons buttons = row.AddComponent<OpeningStanceButtons>();
            buttons.backgrounds = Labels.Select((label, index) => buttons.CreateButton(index, label, font)).ToList();
            return buttons;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static int? StanceForButton(int index)
        {
            return index == 0 ? (int?)null : index - 1;
        }

        private static int ButtonForStance(int? stance)
        {
            return stance.HasValue ? stance.Value + 1 : 0;
        }

        private static Color ColorFor(int index, int selected)
        {
            return index == selected ? Selected : Normal;
        }

        private Image CreateButton(int index, string text, TMP_FontAsset font)
        {
            GameObject go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            Place((RectTransform)go.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(index * (ButtonWidth + Gap), 0f), new Vector2(ButtonWidth, ButtonHeight));

            Image image = go.GetComponent<Image>();
            image.color = Normal;

            int? stance = StanceForButton(index);
            go.GetComponent<Button>().onClick.AddListener(() => Select(stance));

            GameObject labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            Stretch((RectTransform)labelGo.transform);

            TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }

            label.text = text;
            label.fontSize = FontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            return image;
        }

        internal void Bind(UIEquipDeckCardList owner, BookModel newBook)
        {
            list = owner;
            book = newBook;
            bool show = PurpleTear.IsPurpleTear(book);
            gameObject.SetActive(show);
            if (show)
            {
                Recolor();
            }
        }

        private void Select(int? stance)
        {
            if (book == null)
            {
                return;
            }

            OpeningStanceStore.Set(book, stance);
            if (list != null)
            {
                list.changed = true;
            }

            UISoundManager.instance.PlayEffectSound(UISoundType.Card_Apply);
            Recolor();
        }

        private void Recolor()
        {
            int selected = ButtonForStance(OpeningStanceStore.Get(book));
            foreach (var button in backgrounds.Select((image, index) => new { image, color = ColorFor(index, selected) }))
            {
                button.image.color = button.color;
            }
        }
    }
}
