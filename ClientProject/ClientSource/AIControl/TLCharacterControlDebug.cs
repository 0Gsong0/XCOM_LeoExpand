using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeraDeepOcean
{
    public static class TLCharacterControlDebug
    {
        public static void Init(Harmony harmony)
        {
            var drawFront = AccessTools.Method(typeof(Character), nameof(Character.DrawFront), new[]
            {
                typeof(SpriteBatch),
                typeof(Camera),
            });
            if (drawFront != null && Screen.Selected is SubEditorScreen)
            {
                harmony.Patch(drawFront, postfix: new HarmonyMethod(typeof(TLCharacterControlDebug),nameof(OnCharacterDrawFront)));
            }
        }
        private static void OnCharacterDrawFront(Character __instance, SpriteBatch spriteBatch, Camera cam)
        {
            if (GUI.DisableHUD) return;
            if (__instance == null || __instance.Removed || __instance.IsDead || !__instance.Enabled) return;
            if (!TLCharacterControlSystem.TryGetState(__instance.ID, out TLCharacterControlState? state)) return;
            if (state == null || state.Mode == TLCharacterAiMode.Vanilla) return;
            if (state.TargetSubId == Entity.NullEntityID) return;

            DrawLine(__instance, state, spriteBatch);
            DrawStateText(__instance, state, spriteBatch, cam);
        }
        private static void DrawLine(Character character, TLCharacterControlState state, SpriteBatch spriteBatch)
        {
            Vector2 targetWorldPosition = TLCharacterControlSystem.GetTargetWorldPosition(state);

            //Character.DrawFront 的 SpriteBatch 使用世界绘制坐标，并且 Y 轴需要反转。
            Vector2 characterDrawPosition = character.DrawPosition;
            characterDrawPosition.Y = -characterDrawPosition.Y;

            Vector2 targetDrawPosition = targetWorldPosition;
            targetDrawPosition.Y = -targetDrawPosition.Y;

            Color lineColor = state.Mode switch
            {
                TLCharacterAiMode.MoveTo => Color.DeepSkyBlue,
                TLCharacterAiMode.Guard => Color.Gold,
                TLCharacterAiMode.Disabled => Color.Gray,
                _ => Color.White
            };

            GUI.DrawLine(spriteBatch, characterDrawPosition, targetDrawPosition, lineColor * 0.8f,width: 3.0f);
            //在目标位置画一个十字
            const float markerSize = 20.0f;
            GUI.DrawLine(spriteBatch, targetDrawPosition - Vector2.UnitX * markerSize, targetDrawPosition +Vector2.UnitX * markerSize, lineColor, width: 3.0f);
            GUI.DrawLine(spriteBatch, targetDrawPosition - Vector2.UnitY * markerSize, targetDrawPosition +Vector2.UnitY * markerSize, lineColor, width: 3.0f);
        }
        private static void DrawStateText(Character character, TLCharacterControlState state, SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 targetWorldPosition = TLCharacterControlSystem.GetTargetWorldPosition(state);
            float distance = Vector2.Distance(character.WorldPosition, targetWorldPosition);
            string text = 
                $"[当前模式:{state.Mode}]"+
                $"[目标={state.TargetLocalX}:{state.TargetLocalY}]"+
                $"[距离={distance}]";
            Limb? head = character.AnimController.GetLimb(LimbType.Head);
            Vector2 drawPosition = head?.body?.DrawPosition + Vector2.UnitY * 10f ?? character.DrawPosition + Vector2.UnitY * 100f;
            drawPosition.Y = -drawPosition.Y;
            float scale = Math.Clamp(
               1.0f / cam.Zoom,
               0.5f,
               2.0f);
            Vector2 textSize = GUIStyle.SmallFont.MeasureString(text);
            Vector2 origin = new(textSize.X * 0.5f, textSize.Y + 20.0f);
            Color textColor = state.Mode switch
            {
                TLCharacterAiMode.MoveTo =>
                    Color.DeepSkyBlue,

                TLCharacterAiMode.Guard =>
                    Color.Gold,

                TLCharacterAiMode.Disabled =>
                    Color.Gray,

                _ => Color.White
            };
            GUIStyle.SmallFont.DrawString(spriteBatch, text, drawPosition + new Vector2(scale, scale), Color.Black, 0.0f, origin, scale, SpriteEffects.None, 0.001f);

            GUIStyle.SmallFont.DrawString(
                spriteBatch,
                text,
                drawPosition,
                textColor,
                0.0f,
                origin,
                scale,
                SpriteEffects.None,
                0.0f);
        }
    }
}
