using System;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Data
{
	/// <summary>
	/// <para>Separates navigation settings into distinct levels.</para>
	/// <para>Primary Navigation requires rotation</para>
	/// <para>Secondary Navigation does not require rotation</para>
	/// <para>Tertiary Navigation need not be completed</para>
	/// </summary>
	public class AllNavigationSettings
	{
		//[Flags]
		//public enum PathfinderPermissions : byte
		//{
		//	None = 0,
		//	ChangeCourse = 1 << 0,
		//	All = ChangeCourse
		//}

		//[Flags]
		//public enum MovementType : byte
		//{
		//	None = 0,
		//	Rotate = 1 << 0,
		//	Move = 1 << 1,
		//	All = Rotate | Move
		//}

		public class SettingsLevel
		{
			private SettingsLevel parent;

			private PseudoBlock m_navigationBlock;
			private PseudoBlock m_landingBlock;

			private INavigatorMover m_navigatorMover;
			private INavigatorRotator m_navigatorRotator;
			private BlockNameOrientation m_destBlock;

			private DateTime? m_waitUntil;

			private Vector3D? m_destinationOffset;

			//private PathfinderPermissions? m_pathPerm;
			//private MovementType? m_allowedMovement;

			private float? m_destRadius, m_distance, m_distanceAngle, m_speedTarget;

			private bool? m_ignoreAsteroid;//, m_jumpToDest;

			/// <summary>
			/// Creates the top-level SettingLevel, which has defaults set.
			/// </summary>
			internal SettingsLevel(IMyCubeBlock NavBlock)
			{
				m_navigationBlock = new PseudoBlock(NavBlock);

				m_waitUntil = DateTime.UtcNow.AddSeconds(1);

				m_destinationOffset = Vector3.Zero;

				//m_allowedMovement = MovementType.All;
				//m_pathPerm = PathfinderPermissions.All;

				m_destRadius = 100f;
				m_distance = float.NaN;
				m_distanceAngle = float.NaN;
				m_speedTarget = 100f;

				m_ignoreAsteroid = false;
				//m_jumpToDest = false;
			}

			/// <summary>
			/// Creates a SettingLevel with a parent. Where values are not present, value from parent will be used.
			/// </summary>
			internal SettingsLevel(SettingsLevel parent)
			{ this.parent = parent; }

			/// <summary>The navigation block chosen by the player or else the controller block.</summary>
			public PseudoBlock NavigationBlock
			{
				get { return m_navigationBlock ?? parent.NavigationBlock; }
				set { m_navigationBlock = value; }
			}

			/// <summary>
			/// May be null
			/// </summary>
			public PseudoBlock LandingBlock
			{
				get
				{
					if (parent == null)
						return m_landingBlock;
					return m_landingBlock ?? parent.LandingBlock;
				}
				set { m_landingBlock = value; }
			}

			/// <summary>
			/// <para>May be null</para>
			/// </summary>
			public INavigatorMover NavigatorMover
			{
				get
				{
					if (parent == null)
						return m_navigatorMover;
					return m_navigatorMover ?? parent.NavigatorMover;
				}
				set { m_navigatorMover = value; }
			}

			/// <summary>
			/// <para>May be null</para>
			/// </summary>
			public INavigatorRotator NavigatorRotator
			{
				get
				{
					if (parent == null)
						return m_navigatorRotator;
					return m_navigatorRotator ?? parent.NavigatorRotator;
				}
				set { m_navigatorRotator = value; }
			}

			public DateTime WaitUntil
			{
				get { return m_waitUntil ?? parent.WaitUntil; }
				set { m_waitUntil = value; }
			}

			public Vector3D DestinationOffset
			{
				get { return m_destinationOffset ?? parent.DestinationOffset; }
				set { m_destinationOffset = value; }
			}

			//public PathfinderPermissions PathPerm
			//{
			//	get { return m_pathPerm ?? parent.PathPerm; }
			//	set { m_pathPerm = value; }
			//}

			//public MovementType AllowedMovement
			//{
			//	get { return m_allowedMovement ?? parent.AllowedMovement; }
			//	set { m_allowedMovement = value; }
			//}

			/// <summary>
			/// May be null
			/// </summary>
			public BlockNameOrientation DestinationBlock
			{
				get
				{
					if (parent == null)
						return m_destBlock;
					return m_destBlock ?? parent.DestinationBlock;
				}
				set { m_destBlock = value; }
			}

			public float DestinationRadius
			{
				get { return m_destRadius ?? parent.DestinationRadius; }
				set { m_destRadius = value; }
			}

			/// <summary>
			/// Will be NaN if Mover.Move() has not been called
			/// </summary>
			public float Distance
			{
				get { return m_distance ?? parent.Distance; }
				set { m_distance = value; }
			}

			/// <summary>
			/// Will be NaN if Mover.Rotate() has not been called
			/// </summary>
			public float DistanceAngle
			{
				get { return m_distanceAngle ?? parent.DistanceAngle; }
				set { m_distanceAngle = value; }
			}

			/// <summary>The desired speed of the ship.</summary>
			public float SpeedTarget
			{
				get { return m_speedTarget ?? parent.SpeedTarget; }
				set { m_speedTarget = value; }
			}

			public bool IgnoreAsteroid
			{
				get { return m_ignoreAsteroid ?? parent.IgnoreAsteroid; }
				set { m_ignoreAsteroid = value; }
			}

			//public bool JumpToDest
			//{
			//	get { return m_jumpToDest ?? parent.JumpToDest; }
			//	set { m_jumpToDest = value; }
			//}
		}

		private readonly IMyCubeBlock defaultNavBlock;

		///// <summary>Settings that are reset when Autopilot gains control. Settings should be written here but not read.</summary>
		//public SettingsLevel Settings_GainControl { get; private set; }

		/// <summary>Settings that are reset at the start of commands. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Commands { get; private set; }

		/// <summary>Settings that are reset when a primary task is completed. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Task_Primary { get; private set; }

		/// <summary>Settings that are reset when a secondary task is completed. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Task_Secondary { get; private set; }

		/// <summary>Settings that are reset when a tertiary task is completed. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Task_Tertiary { get; private set; }

		///// <summary>Settings that are reset every time autopilot is updated. Settings should be written here but not read.</summary>
		//public SettingLevel MySettings_Update { get; private set; }

		/// <summary>Reflects the current state of autopilot. Settings should be read here but not written.</summary>
		public SettingsLevel Settings_Current { get { return Settings_Task_Tertiary; } }

		public PseudoBlock LastLandingBlock { get; set; }

		public AllNavigationSettings(IMyCubeBlock defaultNavBlock)
		{
			this.defaultNavBlock = defaultNavBlock;
			OnStartOfCommands();
		}

		//public void OnGainControl()
		//{
		//	Settings_GainControl = new SettingsLevel();
		//	OnStartOfCommands();
		//}

		public void OnStartOfCommands()
		{
			Settings_Commands = new SettingsLevel(defaultNavBlock);
			OnTaskPrimaryComplete();
		}

		public void OnTaskPrimaryComplete()
		{
			Settings_Task_Primary = new SettingsLevel(Settings_Commands);
			OnTaskSecondaryComplete();
		}

		public void OnTaskSecondaryComplete()
		{
			Settings_Task_Secondary = new SettingsLevel(Settings_Task_Primary);
			OnTaskTertiaryComplete();
		}

		public void OnTaskTertiaryComplete()
		{
			Settings_Task_Tertiary = new SettingsLevel(Settings_Task_Secondary);
			//OnUpdate();
		}

		//public void OnUpdate()
		//{
		//	MySettings_Update = new SettingLevel(MySettings_Subtask);
		//}

		public string PrettyDistance()
		{
			float distance = Settings_Current.Distance;
			if (float.IsNaN(distance))
				return string.Empty;
			if (distance < 1f)
				return "0 m";
			return PrettySI.makePretty(distance) + 'm';
		}

		public bool DirectionMatched()
		{
			float angle = Settings_Current.DistanceAngle;
			if (float.IsNaN(angle))
				return false;
			return angle < 0.1f;
		}

		public bool DistanceLessThan(float value)
		{
			float distance = Settings_Current.Distance;
			if (float.IsNaN(distance))
				return false;
			return distance < value;
		}

		public bool DistanceLessThanDestRadius()
		{
			return DistanceLessThan(Settings_Current.DestinationRadius);
		}

	}
}
