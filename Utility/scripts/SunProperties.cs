using System;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public class SunProperties
	{
		private static readonly DateTime StartOfGame = new DateTime(2081, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private static SunProperties Instance;

		private readonly Logger myLogger = new Logger("SunProperties");
		private readonly Vector3 DefSunDirection;

		private readonly float SunRotationIntervalMinutes;
		private readonly bool EnableSunRotation;

		private Vector3 mySunDirection;
		private readonly FastResourceLock lock_mySunDirection = new FastResourceLock();

		public SunProperties()
		{
			MyObjectBuilder_EnvironmentDefinition environmentDefinition = MyDefinitionManager.Static.EnvironmentDefinition.GetObjectBuilder() as MyObjectBuilder_EnvironmentDefinition;
			DefSunDirection = environmentDefinition.SunDirection;

			SunRotationIntervalMinutes = MyAPIGateway.Session.SessionSettings.SunRotationIntervalMinutes;
			EnableSunRotation = MyAPIGateway.Session.SessionSettings.EnableSunRotation;
			mySunDirection = DefSunDirection;

			myLogger.debugLog("Definition SunDirection: " + mySunDirection + ", EnableSunRotation: " + EnableSunRotation + ", SunRotationIntervalMinutes: " + SunRotationIntervalMinutes, "SunProperties()", Logger.severity.INFO);
			Instance = this;
		}

		public void Update10()
		{
			MyAPIGateway.Parallel.Start(SetSunDirection);
		}

		public static Vector3 SunDirection
		{
			get
			{
				using (Instance.lock_mySunDirection.AcquireSharedUsing())
					return Instance.mySunDirection;
			}
		}

		private TimeSpan ElapsedGameTime
		{
			get
			{
				TimeSpan result = TimeSpan.Zero;
				MainLock.UsingShared(() => {
					result = MyAPIGateway.Session.GameDateTime - StartOfGame;
				});
				return result;
			}
		}

		/// <remarks>
		/// Code copied from Sandbox.Game.Gui.MyGuiScreenGamePlay.Draw()
		/// </remarks>
		private void SetSunDirection()
		{
			Vector3 sunDirection = -DefSunDirection;
			if (EnableSunRotation)
			{
				float angle = 2.0f * MathHelper.Pi * (float)(ElapsedGameTime.TotalMinutes / SunRotationIntervalMinutes);
				float originalSunCosAngle = Math.Abs(Vector3.Dot(sunDirection, Vector3.Up));
				Vector3 sunRotationAxis;
				if (originalSunCosAngle > 0.95f)
				{
					// original sun is too close to the poles
					sunRotationAxis = Vector3.Cross(Vector3.Cross(sunDirection, Vector3.Left), sunDirection);
				}
				else
				{
					sunRotationAxis = Vector3.Cross(Vector3.Cross(sunDirection, Vector3.Up), sunDirection);
				}
				sunDirection = Vector3.Transform(sunDirection, Matrix.CreateFromAxisAngle(sunRotationAxis, angle));
				sunDirection.Normalize();

				using (Instance.lock_mySunDirection.AcquireExclusiveUsing())
					mySunDirection = -sunDirection;
			}
			//myLogger.debugLog("Sun Direction: " + (-sunDirection), "SetSunDirection()");
		}
	}
}
