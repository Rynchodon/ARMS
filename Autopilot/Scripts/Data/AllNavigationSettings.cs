using System;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Data
{
	/// <summary>
	/// <para>Separates navigation settings into distinct levels.</para>
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

		public class SettingsLevel
		{
			private SettingsLevel parent;

			private PseudoBlock m_navigationBlock;
			private PseudoBlock m_landingBlock;

			private EnemyFinder m_enemyFinder;
			private INavigatorMover m_navigatorMover;
			private INavigatorRotator m_navigatorRotator;
			private BlockNameOrientation m_destBlock;
			private IMyEntity m_destEntity;

			private DateTime? m_waitUntil;

			private Vector3D? m_destinationOffset;
			//private Vector3D? m_destination;

			//private PathfinderPermissions? m_pathPerm;

			private float? m_destRadius, m_distance, m_distanceAngle, m_speedTarget;

			private bool? m_ignoreAsteroid, m_destChanged, m_collisionAvoidance;

			/// <summary>
			/// Creates the top-level SettingLevel, which has defaults set.
			/// </summary>
			internal SettingsLevel(IMyCubeBlock NavBlock)
			{
				m_navigationBlock = new PseudoBlock(NavBlock);

				m_waitUntil = DateTime.UtcNow.AddSeconds(1);

				m_destinationOffset = Vector3D.Zero;
				//m_destination = Vector3D.NegativeInfinity;

				//m_pathPerm = PathfinderPermissions.All;

				m_destRadius = 100f;
				m_distance = float.NaN;
				m_distanceAngle = float.NaN;
				m_speedTarget = 100f;

				m_ignoreAsteroid = false;
				m_destChanged = true;
				m_collisionAvoidance = true;
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
			/// <para>May be null</para>
			/// <para>The block that will be landed</para>
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

			public EnemyFinder EnemyFinder
			{
				get
				{
					if (parent == null)
						return m_enemyFinder;
					return m_enemyFinder ?? parent.EnemyFinder;
				}
				set { m_enemyFinder = value; }
			}

			/// <summary>
			/// <para>May be null</para>
			/// <para>The navigator that is moving the ship</para>
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
			/// <para>The navigator that is rotating the ship.</para>
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

			/// <summary>ShipController will not run the navigator until after this time.</summary>
			public DateTime WaitUntil
			{
				get { return m_waitUntil ?? parent.WaitUntil; }
				set { m_waitUntil = value; }
			}

			/// <summary>Added to position of target block</summary>
			public Vector3D DestinationOffset
			{
				get { return m_destinationOffset ?? parent.DestinationOffset; }
				set { m_destinationOffset = value; }
			}

			//public Vector3D Destination
			//{
			//	get { return m_destination ?? parent.Destination; }
			//	set { m_destination = value; }
			//}

			//public PathfinderPermissions PathPerm
			//{
			//	get { return m_pathPerm ?? parent.PathPerm; }
			//	set { m_pathPerm = value; }
			//}

			/// <summary>
			/// <para>May be null</para>
			/// <para>Information about which block to target</para>
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

			/// <summary>
			/// <para>The entity that is the target of the navigator, will be ignored by pathfinder.</para>
			/// <para>Block is used instead of grid.</para>
			/// </summary>
			public IMyEntity DestinationEntity
			{
				get
				{
					if (parent == null)
						return m_destEntity;
					return m_destEntity ?? parent.DestinationEntity;
				}
				set { m_destEntity = value; }
			}

			/// <summary>How close the navigation block needs to be to the destination</summary>
			public float DestinationRadius
			{
				get { return m_destRadius ?? parent.DestinationRadius; }
				set { m_destRadius = value; }
			}

			/// <summary>
			/// <para>Will be NaN if Mover.Move() has not been called</para>
			/// <para>Distance between current position and destination.</para>
			/// </summary>
			public float Distance
			{
				get { return m_distance ?? parent.Distance; }
				set { m_distance = value; }
			}

			/// <summary>
			/// <para>Will be NaN if Mover.Rotate() has not been called</para>
			/// <para>Angular distance between current direction and desired direction.</para>
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

			/// <summary>Pathfinder should not run voxel tests.</summary>
			public bool IgnoreAsteroid
			{
				get { return m_ignoreAsteroid ?? parent.IgnoreAsteroid; }
				set { m_ignoreAsteroid = value; }
			}

			/// <summary>Pathfinder uses this to track when OnTaskTertiaryComplete() is invoked.</summary>
			public bool DestinationChanged
			{
				get { return m_destChanged ?? parent.DestinationChanged; }
				set { m_destChanged = value; }
			}

			public bool CollisionAvoidance
			{
				get { return m_collisionAvoidance ?? parent.CollisionAvoidance; }
				set { m_collisionAvoidance = value; }
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

		/// <summary>Settings that are reset when a navigator rotator is finished. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Task_NavRot { get; private set; }

		/// <summary>Settings that are reset when a navigator mover is finished. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Task_NavMove { get; private set; }

		/// <summary>Settings that are reset when an engager is finished. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Task_NavEngage { get; private set; }

		/// <summary>Settings that are reset when a waypoint navigator is finished. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Task_NavWay { get; private set; }

		///// <summary>Settings that are reset every time autopilot is updated. Settings should be written here but not read.</summary>
		//public SettingLevel MySettings_Update { get; private set; }

		/// <summary>Reflects the current state of autopilot. Settings should be read here but not written.</summary>
		public SettingsLevel Settings_Current { get { return Settings_Task_NavWay; } }

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
			OnTaskComplete_NavRot();
		}

		public void OnTaskComplete_NavRot()
		{
			Settings_Task_NavRot = new SettingsLevel(Settings_Commands);
			OnTaskComplete_NavMove();
		}

		public void OnTaskComplete_NavMove()
		{
			Settings_Task_NavMove = new SettingsLevel(Settings_Task_NavRot);
			OnTaskComplete_NavEngage();
		}

		public void OnTaskComplete_NavEngage()
		{
			Settings_Task_NavEngage = new SettingsLevel(Settings_Task_NavMove);
			OnTaskComplete_NavWay();
		}

		public void OnTaskComplete_NavWay()
		{
			Settings_Task_NavWay = new SettingsLevel(Settings_Task_NavEngage);
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
