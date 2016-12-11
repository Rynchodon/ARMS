#if DEBUG
// if this is enabled, stop AeroProfiler from disposing of m_data
//#define DEBUG_DRAW
#define DEBUG_IN_SPACE
#endif

using System;
using System.Reflection;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Gui;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Movement
{
	class AeroEffects
	{

		private const ulong ProfileWait = 200uL;

		private static FieldInfo MyGuiScreenHudSpace_Static;
		private static MethodInfo MyGuiScreenHudSpace_DrawGravityVectorIndicator;

		private static object[] DrawGravityVectorIndicator_Params = new object[4];
		private static StringBuilder DrawTitle = new StringBuilder("Air Speed & Drag");

		static AeroEffects()
		{
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

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;

		private ulong m_profileAt;
		private float m_airDensity;
		private bool m_cockpitComplain;
		private Vector3D m_worldDrag;
#if DEBUG_DRAW
		private bool m_profilerDebugDraw;
		private int m_maxSteps = int.MaxValue;
#endif

		private AeroProfiler value_profiler;
		private AeroProfiler m_profiler
		{
			get { return value_profiler; }
			set
			{
#if DEBUG_DRAW
				DebugDraw(false);
#endif
				value_profiler = value;
			}
		}

		public Vector3[] DragCoefficient { get; private set; }

		public AeroEffects(IMyCubeGrid grid)
		{
			this.m_logger = new Logger(grid);
			this.m_grid = grid;
			this.m_profileAt = Globals.UpdateCount + ProfileWait;

			m_grid.OnBlockAdded += OnBlockChange;
			m_grid.OnBlockRemoved += OnBlockChange;
#if DEBUG_DRAW
			MyAPIGateway.Utilities.MessageEntered += Utilities_MessageEntered;
			m_grid.OnClose += OnGridClose;
#endif
		}

#if DEBUG_DRAW
		private void Utilities_MessageEntered(string messageText, ref bool sendToOthers)
		{
			int steps;
			if (!int.TryParse(messageText, out steps) || steps == m_maxSteps)
				return;
			m_maxSteps = steps;
			m_logger.debugLog("Running profiler", Logger.severity.DEBUG);
			m_profileAt = ulong.MaxValue;
			m_profiler = new AeroProfiler(m_grid, m_maxSteps);
		}
#endif

		public void Update1()
		{
			ApplyDrag();
			Draw();
		}

		private void ApplyDrag()
		{
			if (DragCoefficient == null || m_airDensity == 0f)
			{
				m_worldDrag = Vector3D.Zero;
				return;
			}

			Vector3 worldVelocity = m_grid.Physics.LinearVelocity;

			if (worldVelocity.LengthSquared() < 1f)
			{
				m_worldDrag = Vector3D.Zero;
				return;
			}

			MatrixD worldInv = m_grid.WorldMatrixNormalizedInv.GetOrientation();
			Vector3D localVelocityD; Vector3D.Transform(ref worldVelocity, ref worldInv, out localVelocityD);
			Vector3 localVelocity = localVelocityD;

			Vector3 localDrag = Vector3.Zero;
			for (int i = 0; i < 6; ++i)
			{
				Base6Directions.Direction direction = (Base6Directions.Direction)i;
				Vector3 vectorDirection; Base6Directions.GetVector(direction, out vectorDirection);
				float dot; Vector3.Dot(ref localVelocity, ref vectorDirection, out dot);
				// intentionally keeping negative values
				Vector3 scaled; Vector3.Multiply(ref DragCoefficient[i], Math.Sign(dot) * dot * dot, out scaled);
				Vector3.Add(ref localDrag, ref scaled, out localDrag);

				m_logger.traceLog("direction: " + direction + ", dot: " + dot + ", scaled: " + scaled);
			}

			MatrixD world = m_grid.WorldMatrix.GetOrientation();
			Vector3D.Transform(ref localDrag, ref world, out m_worldDrag);

			m_logger.traceLog("world velocity: " + worldVelocity + ", local velocity: " + localVelocity + ", local drag: " + localDrag + ", world drag: " + m_worldDrag);

			m_grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, m_worldDrag, null, null);
		}

		public void Update100()
		{
			FillAirDensity();
			if (m_profileAt <= Globals.UpdateCount && !m_grid.IsStatic && m_airDensity != 0f)
			{
				m_logger.debugLog("Running profiler", Logger.severity.DEBUG);
				m_profileAt = ulong.MaxValue;
				SetCockpitComplain(true);
#if DEBUG_DRAW
				m_profiler = new AeroProfiler(m_grid, m_maxSteps);
#else
				m_profiler = new AeroProfiler(m_grid);
#endif
				return;
			}
#if DEBUG_DRAW
			if (m_profiler != null && !m_profiler.Running && m_profiler.Success)
				DebugDraw(true);
#endif
			if (m_profiler != null && !m_profiler.Running)
			{
				if (m_profiler.Success)
				{
					DragCoefficient = m_profiler.DragCoefficient;
					SetCockpitComplain(false);
				}
				m_profiler = null;
			}
		}

		private void FillAirDensity()
		{
#if DEBUG_IN_SPACE
			m_airDensity = 1f;
#else
			Vector3D gridCentre = m_grid.GetCentre();
			MyPlanet closestPlanet = MyPlanetExtensions.GetClosestPlanet(gridCentre);
			if (closestPlanet == null)
				m_airDensity = 0f;
			else
				m_airDensity = closestPlanet.GetAirDensity(gridCentre);
#endif
		}

		private void OnBlockChange(IMySlimBlock obj)
		{
			m_profileAt = Globals.UpdateCount + ProfileWait;
		}

		private void SetCockpitComplain(bool enabled)
		{
			if (m_cockpitComplain == enabled)
				return;
			m_cockpitComplain = enabled;

			CubeGridCache cache = CubeGridCache.GetFor(m_grid);
			if (cache == null)
				return;
			foreach (MyCockpit cockpit in cache.BlocksOfType(typeof(MyObjectBuilder_Cockpit)))
			{
				if (m_cockpitComplain)
					cockpit.AppendingCustomInfo += CockpitComplain;
				else
					cockpit.AppendingCustomInfo -= CockpitComplain;
				cockpit.RefreshCustomInfo();
			}
		}

		private void CockpitComplain(Sandbox.Game.Entities.Cube.MyTerminalBlock arg1, StringBuilder arg2)
		{
			arg2.AppendLine("ARMS is calculating drag");
		}

		private void Draw()
		{
			MyPlayer player = MySession.Static.LocalHumanPlayer;
			if (player == null)
				return;

			MyCubeBlock block = player.Controller.ControlledEntity as MyCubeBlock;
			if (block == null || block.CubeGrid != m_grid)
				return;

			if (m_worldDrag == Vector3D.Zero)
				return;

			MyGuiPaddedTexture backgroundTexture = MyGuiConstants.TEXTURE_HUD_BG_MEDIUM_DEFAULT;

			Vector2 backgroundSize = backgroundTexture.SizeGui + new Vector2(0.017f, 0.05f);
			backgroundSize.X *= 0.8f;
			backgroundSize.Y *= 0.75f;

			Vector2 dividerLineSize = new Vector2(backgroundSize.X - backgroundTexture.PaddingSizeGui.X, backgroundSize.Y / 60f);

			Vector2 backgroundPosition = new Vector2(0.01f, backgroundSize.Y + 0.04f);
			backgroundPosition = ConvertHudToNormalizedGuiPosition(ref backgroundPosition);

			Vector2 titleTextPos = backgroundPosition + backgroundSize * new Vector2(0.94f, -1f) + backgroundTexture.PaddingSizeGui * Vector2.UnitY * 0.2f;
			Vector2 dividerLinePosition = new Vector2(backgroundPosition.X + backgroundTexture.PaddingSizeGui.X * 0.5f, titleTextPos.Y - 0.022f) + new Vector2(0.0f, 0.026f);

			Vector2 vectorPosition = backgroundPosition - backgroundSize * new Vector2(-0.5f, 0.55f) + backgroundTexture.PaddingSizeGui * Vector2.UnitY * 0.5f;
			vectorPosition = MyGuiManager.GetHudSize() * ConvertNormalizedGuiToHud(ref vectorPosition);

			MyGuiManager.DrawSpriteBatch(MyGuiConstants.TEXTURE_HUD_BG_MEDIUM_DEFAULT.Texture, backgroundPosition, backgroundSize + new Vector2(0f, 0.025f), Color.White, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_BOTTOM);
			MyGuiManager.DrawString(MyFontEnum.White, DrawTitle, titleTextPos, MyGuiConstants.HUD_TEXT_SCALE, drawAlign: MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_BOTTOM);
			MyGuiManager.DrawSpriteBatch(MyGuiConstants.TEXTURE_HUD_GRAVITY_LINE.Texture, dividerLinePosition, dividerLineSize, Color.White, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);

			object obj = MyGuiScreenHudSpace_Static.GetValue(null);

			DrawGravityVectorIndicator_Params[0] = vectorPosition;
			DrawGravityVectorIndicator_Params[1] = Vector3.Normalize(-m_grid.Physics.LinearVelocity);
			DrawGravityVectorIndicator_Params[3] = Color.GreenYellow;
			MyGuiScreenHudSpace_DrawGravityVectorIndicator.Invoke(obj, DrawGravityVectorIndicator_Params);

			DrawGravityVectorIndicator_Params[1] = Vector3.Normalize(m_worldDrag);
			DrawGravityVectorIndicator_Params[3] = Color.Red;
			MyGuiScreenHudSpace_DrawGravityVectorIndicator.Invoke(obj, DrawGravityVectorIndicator_Params);
		}

		private static Vector2 ConvertHudToNormalizedGuiPosition(ref Vector2 hudPos)
		{
			var safeFullscreenRectangle = MyGuiManager.GetSafeFullscreenRectangle();
			var safeFullscreenSize = new Vector2(safeFullscreenRectangle.Width, safeFullscreenRectangle.Height);
			var safeFullscreenOffset = new Vector2(safeFullscreenRectangle.X, safeFullscreenRectangle.Y);

			var safeGuiRectangle = MyGuiManager.GetSafeGuiRectangle();
			var safeGuiSize = new Vector2(safeGuiRectangle.Width, safeGuiRectangle.Height);
			var safeGuiOffset = new Vector2(safeGuiRectangle.X, safeGuiRectangle.Y);

			return ((hudPos * safeFullscreenSize + safeFullscreenOffset) - safeGuiOffset) / safeGuiSize;
		}

		private static Vector2 ConvertNormalizedGuiToHud(ref Vector2 normGuiPos)
		{
			var safeFullscreenRectangle = MyGuiManager.GetSafeFullscreenRectangle();
			var safeFullscreenSize = new Vector2(safeFullscreenRectangle.Width, safeFullscreenRectangle.Height);
			var safeFullscreenOffset = new Vector2(safeFullscreenRectangle.X, safeFullscreenRectangle.Y);

			var safeGuiRectangle = MyGuiManager.GetSafeGuiRectangle();
			var safeGuiSize = new Vector2(safeGuiRectangle.Width, safeGuiRectangle.Height);
			var safeGuiOffset = new Vector2(safeGuiRectangle.X, safeGuiRectangle.Y);

			return ((normGuiPos * safeGuiSize + safeGuiOffset) - safeFullscreenOffset) / safeFullscreenSize;
		}

#if DEBUG_DRAW
		private void OnGridClose(VRage.ModAPI.IMyEntity obj)
		{
			m_profiler = null;
			MyAPIGateway.Utilities.MessageEntered -= Utilities_MessageEntered;
		}

		private void DebugDraw(bool enable)
		{
			if (enable == m_profilerDebugDraw)
				return;

			if (enable)
			{
				m_profilerDebugDraw = true;
				UpdateManager.Register(1, m_profiler.DebugDraw_Velocity);
				Logger.DebugNotify("Debug drawing velocity");
			}
			else
			{
				m_profilerDebugDraw = false;
				UpdateManager.Unregister(1, m_profiler.DebugDraw_Velocity);
			}
		}
#endif

	}
}
