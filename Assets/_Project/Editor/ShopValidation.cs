using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class ShopValidation
    {
        public static void CheckInteractions()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Run in Play Mode.");
            var shop = Object.FindFirstObjectByType<ShopScreen>();
            Check(shop != null && shop.Offers.Length == 1 && shop.Packs.Length == 3, "Shop inventory");
            var ledger = new CaseRewardLedger(null, () => 0, _ => throw new Exception("Purchase must not write"));
            Check(AvatarCatalog.At(0).Kind == AvatarPurchaseKind.Dollars && AvatarCatalog.At(0).Price == 599, "Sonic price");
            Check(!ledger.OwnsAvatar("sonic") && ledger.OwnsAvatar("madam-engry"), "Premium and included ownership");
            Check(!ledger.TryBuyAvatar("sonic"), "Dollar purchase requires store integration");
            long gems = CaseRewards.GemsBalance, aura = CaseRewards.AuraBalance;
            shop.Entry.onClick.Invoke(); Check(shop.IsOpen, "Lobby entry");
            shop.Offers[0].onClick.Invoke();
            Check(!shop.IsOpen && shop.Collection.IsOpen && shop.Collection.InfoPanel.activeSelf, "Offer preview");
            Check(!shop.Collection.PreviewPage.Action.interactable && shop.Collection.PreviewPage.ActionText.text.Contains("5.99"), "Purchase unavailable and price visible");
            shop.Collection.InfoClose.onClick.Invoke(); Check(shop.IsOpen && !shop.Collection.IsOpen, "Preview back to shop");
            shop.Info[0].onClick.Invoke(); Check(shop.Collection.InfoPanel.activeSelf, "Info preview");
            shop.Collection.PreviewPage.Home.onClick.Invoke(); Check(!shop.IsOpen && !shop.Collection.IsOpen, "Preview home to lobby");
            shop.Entry.onClick.Invoke();
            for (int i = 0; i < shop.Packs.Length; i++)
            {
                shop.Packs[i].onClick.Invoke();
                Check(shop.PackDialog.activeSelf && shop.PackTitle.text == ShopScreen.GemAmounts[i] + " GEMS", "Pack details");
                Check(shop.PackImage.sprite == shop.PackSprites[i] && shop.PackPrice.text == ShopScreen.GemPrices[i], "Pack art and price");
                shop.DialogClose.onClick.Invoke(); Check(!shop.PackDialog.activeSelf && shop.IsOpen, "Pack dismiss");
            }
            shop.Okay.onClick.Invoke(); Check(!shop.IsOpen, "OK to lobby");
            shop.Entry.onClick.Invoke(); shop.Back.onClick.Invoke(); Check(!shop.IsOpen, "Back to lobby");
            shop.Entry.onClick.Invoke(); shop.Home.onClick.Invoke(); Check(!shop.IsOpen, "Home to lobby");
            shop.Collection.Show(); shop.Collection.Cards[0].onClick.Invoke();
            Check(shop.Collection.InfoPanel.activeSelf && !shop.Collection.PreviewPage.Action.interactable, "Collection uses same paid offer");
            shop.Collection.InfoClose.onClick.Invoke(); Check(shop.Collection.IsOpen && !shop.IsOpen, "Normal collection back unaffected");
            shop.Collection.Hide(); shop.Show();
            Check(gems == CaseRewards.GemsBalance && aura == CaseRewards.AuraBalance, "Browsing did not mutate wallet");
            Directory.CreateDirectory("Logs/Shop");
            File.WriteAllText("Logs/Shop/validation.txt", "PASS: lobby, one Sonic offer, shared preview, Back/Home/OK, three pack dialogs, premium ownership, unavailable payments, unchanged wallet and collection navigation.\n");
            Debug.Log("[ShopValidation] PASS");
        }

        private static void Check(bool okay, string name) { if (!okay) throw new InvalidOperationException(name); }

        public static void Capture(int width, int height, string name)
        {
            var source = Object.FindFirstObjectByType<ShopScreen>();
            var cameraGo = new GameObject("ShopCaptureCamera");
            var canvasGo = new GameObject("ShopCaptureCanvas", typeof(RectTransform), typeof(Canvas));
            canvasGo.SetActive(false);
            var rt = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            try
            {
                var camera = cameraGo.AddComponent<Camera>(); camera.transform.position = new Vector3(5000, 0, -10);
                camera.orthographic = true; camera.orthographicSize = height * .5f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.targetTexture = rt;
                var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 1;
                var copy = Object.Instantiate(source.Overlay, canvasGo.transform);
                foreach (var preview in copy.GetComponentsInChildren<AvatarCardPreview>(true)) preview.enabled = false;
                foreach (var tactile in copy.GetComponentsInChildren<UiTactile>(true))
                { tactile.transform.localScale = Vector3.one; tactile.ResetVisual(); tactile.enabled = false; }
                var safe = copy.GetComponentInChildren<SafeAreaFitter>(true); safe.enabled = false;
                var art = (RectTransform)copy.transform.Find("SafeArea/Art");
                canvasGo.SetActive(true); copy.SetActive(true);
                UiBuilder.Stretch((RectTransform)art.parent, 0, 25, 0, 42);
                Canvas.ForceUpdateCanvases();
                float scale = ((RectTransform)art.parent).rect.width / 390;
                float units = ((RectTransform)art.parent).rect.height / scale;
                art.localScale = Vector3.one * scale; art.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, units);
                ((RectTransform)art.Find("Viewport")).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, units - 202);
                ((RectTransform)art.Find("Okay")).anchoredPosition = new Vector2(140, -(units - 65));
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                Directory.CreateDirectory("Logs/Shop"); File.WriteAllBytes("Logs/Shop/" + name + ".png", texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = old; Object.DestroyImmediate(canvasGo); Object.DestroyImmediate(cameraGo);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(texture);
            }
        }
    }
}
