using System;
using System.Text;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRageMath;
using System.Reflection;
using Sandbox.Game.Gui;
using VRage.Game;
using VRage.Game.Gui;
using VRage.Utils;
using VRage.Game.ModAPI;

namespace Rynchodon.Autopilot.Aerodynamics
{
	/// <summary>
	/// Draws the air indicators in the top left corner of the screen.
	/// </summary>
	static class AeroDrawIndicators
	{

		private readonly static MethodInfo MyGuiScreenHudBase_ConvertNormalizedGuiToHud;
		private readonly static FieldInfo MyGuiScreenHudSpace_Static;
		private readonly static MethodInfo MyGuiScreenHudSpace_DrawGravityVectorIndicator;

		private readonly static object[] DrawGravityVectorIndicator_Params = new object[4];
		private readonly static StringBuilder DrawTitle = new StringBuilder("Air Speed & Lift");
		private readonly static StringBuilder DrawAirDensity = new StringBuilder();

		private static Vector2I screenSize;
		private static Vector2 airDensityTextPos, backgroundPosition, backgroundSize, dividerLinePosition, dividerLineSize, titleTextPos;

		static AeroDrawIndicators()
		{
			MyGuiScreenHudBase_ConvertNormalizedGuiToHud = typeof(MyGuiScreenHudBase).GetMethod("ConvertNormalizedGuiToHud", BindingFlags.Static | BindingFlags.NonPublic);
			if (MyGuiScreenHudBase_ConvertNormalizedGuiToHud == null)
				throw new NullReferenceException("MyGuiScreenHudBase_ConvertNormalizedGuiToHud");

			Type MyGuiScreenHudSpace = typeof(MyGuiScreenHudBase).Assembly.GetType("Sandbox.Game.Gui.MyGuiScreenHudSpace");
			if (MyGuiScreenHudSpace == null)
				throw new NullReferenceException("MyGuiScreenHudSpace");

			MyGuiScreenHudSpace_Static = MyGuiScreenHudSpace.GetField("Static", BindingFlags.Static | BindingFlags.Public);
			if (MyGuiScreenHudSpace_Static == null)
				throw new NullReferenceException("MyGuiScreenHudSpace_Static");

			MyGuiScreenHudSpace_DrawGravityVectorIndicator = MyGuiScreenHudSpace.GetMethod("DrawGravityVectorIndicator", BindingFlags.Instance | BindingFlags.NonPublic);
			if (MyGuiScreenHudSpace_DrawGravityVectorIndicator == null)
				throw new NullReferenceException("MyGuiScreenHudSpace_DrawGravityVectorIndicator");

			DrawGravityVectorIndicator_Params[2] = MyHudTexturesEnum.gravity_arrow;
		}

		/// <summary>
		/// Draw drag indicators in the top left corner of the screen.
		/// Modified from MyGuiScreenHudSpace.DrawGravityIndicator.
		/// </summary>
		public static void DrawDrag(IMyCubeBlock cockpit, ref Vector3 worldDrag, float airDensity)
		{
			Rectangle fullscreen = MyGuiManager.GetFullscreenRectangle();
			if (screenSize.X != fullscreen.Width || screenSize.Y != fullscreen.Width)
			{
				screenSize.X = fullscreen.Width;
				screenSize.Y = fullscreen.Width;

				DrawAirAndDrag_CalcultatePositions();
			}

			DrawAirDensity.Clear();
			DrawAirDensity.Append("Air: ");
			DrawAirDensity.Append(airDensity.ToString("F3"));

			MyGuiManager.DrawSpriteBatch(MyGuiConstants.TEXTURE_HUD_BG_MEDIUM_DEFAULT.Texture, backgroundPosition, backgroundSize, Color.White, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_BOTTOM);
			MyGuiManager.DrawString(MyFontEnum.White, DrawTitle, titleTextPos, MyGuiConstants.HUD_TEXT_SCALE, drawAlign: MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_BOTTOM);
			MyGuiManager.DrawSpriteBatch(MyGuiConstants.TEXTURE_HUD_GRAVITY_LINE.Texture, dividerLinePosition, dividerLineSize, Color.White, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
			MyGuiManager.DrawString(MyFontEnum.White, DrawAirDensity, airDensityTextPos, MyGuiConstants.HUD_TEXT_SCALE, drawAlign: MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_BOTTOM);

			object obj = MyGuiScreenHudSpace_Static.GetValue(null);

			Vector3 backward = cockpit.WorldMatrix.Backward;
			float parasiticDragMag; Vector3.Dot(ref worldDrag, ref backward, out parasiticDragMag);
			Vector3 parasiticDrag; Vector3.Multiply(ref backward, parasiticDragMag, out parasiticDrag);
			Vector3 liftDrag; Vector3.Subtract(ref worldDrag, ref parasiticDrag, out liftDrag);
			Vector3 airSpeed = -cockpit.CubeGrid.Physics.LinearVelocity;

			float length = airSpeed.Normalize();
			int lowValue = 255 - (int)(length * 2.55f); // full colour at 100 m/s
			Color colour = new Color(255, lowValue, lowValue);

			DrawGravityVectorIndicator_Params[1] = airSpeed;
			DrawGravityVectorIndicator_Params[3] = colour;
			MyGuiScreenHudSpace_DrawGravityVectorIndicator.Invoke(obj, DrawGravityVectorIndicator_Params);

			length = liftDrag.Normalize();
			lowValue = 255 - (int)(length * 25.5f / cockpit.CubeGrid.Physics.Mass); // full colour at 10 m/s/s
			colour = new Color(lowValue, 255, lowValue);

			DrawGravityVectorIndicator_Params[1] = liftDrag;
			DrawGravityVectorIndicator_Params[3] = colour;
			MyGuiScreenHudSpace_DrawGravityVectorIndicator.Invoke(obj, DrawGravityVectorIndicator_Params);
		}

		/// <summary>
		/// Modified from MyGuiScreenHudSpace.DrawGravityIndicator.
		/// </summary>
		private static void DrawAirAndDrag_CalcultatePositions()
		{
			MyGuiPaddedTexture backgroundTexture = MyGuiConstants.TEXTURE_HUD_BG_MEDIUM_DEFAULT;

			backgroundSize = backgroundTexture.SizeGui + new Vector2(0.017f, 0.05f);
			backgroundSize.X *= 0.8f;
			backgroundSize.Y *= 0.85f;

			dividerLineSize = new Vector2(backgroundSize.X - backgroundTexture.PaddingSizeGui.X, backgroundSize.Y / 60f);

			backgroundPosition = new Vector2(0.01f, backgroundSize.Y + 0.04f);
			backgroundPosition = MyGuiScreenHudBase.ConvertHudToNormalizedGuiPosition(ref backgroundPosition);

			titleTextPos = backgroundPosition + backgroundSize * new Vector2(0.90f, -1f) + backgroundTexture.PaddingSizeGui * Vector2.UnitY * 0.2f;
			dividerLinePosition = new Vector2(backgroundPosition.X + backgroundTexture.PaddingSizeGui.X * 0.5f, titleTextPos.Y - 0.022f) + new Vector2(0.0f, 0.026f);

			Vector2 vectorPosition = backgroundPosition - backgroundSize * new Vector2(-0.5f, 0.55f) + backgroundTexture.PaddingSizeGui * Vector2.UnitY * 0.5f;
			vectorPosition = MyGuiManager.GetHudSize() * (Vector2)MyGuiScreenHudBase_ConvertNormalizedGuiToHud.Invoke(null, new object[] { vectorPosition });

			airDensityTextPos = backgroundPosition + backgroundSize * new Vector2(0.75f, -0.1f) + backgroundTexture.PaddingSizeGui * Vector2.UnitY * 0.2f;

			backgroundSize += new Vector2(0f, 0.025f);

			DrawGravityVectorIndicator_Params[0] = vectorPosition;
		}

	}
}
