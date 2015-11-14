using System;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Settings;
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

			private float? m_destRadius, m_distance, m_distanceAngle, m_speedTarget, m_speedMaxRelative;

			private bool? m_ignoreAsteroid, m_destChanged, m_collisionAvoidance, m_pathfindeCanChangeCourse, m_formation;

			/// <summary>
			/// Creates the top-level SettingLevel, which has defaults set.
			/// </summary>
			internal SettingsLevel(IMyCubeBlock NavBlock)
			{
				m_navigationBlock = new PseudoBlock(NavBlock);

				m_waitUntil = DateTime.UtcNow.AddSeconds(1);

				m_destinationOffset = Vector3D.Zero;

				m_destRadius = 100f;
				m_distance = float.NaN;
				m_distanceAngle = float.NaN;
				m_speedTarget = ServerSettings.GetSetting<float>(ServerSettings.SettingName.fDefaultSpeed);
				m_speedMaxRelative = float.MaxValue;

				m_ignoreAsteroid = false;
				m_destChanged = true;
				m_collisionAvoidance = true;
				m_pathfindeCanChangeCourse = true;
				m_formation = false;
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

			/// <summary>
			/// <para>The desired speed of the ship</para>
			/// <para>Get returns the lowest value at this or higher level</para>
			/// </summary>
			public float SpeedTarget
			{
				get
				{
					if (parent == null)
						return m_speedTarget.Value;
					if (m_speedTarget.HasValue)
						return Math.Min(m_speedTarget.Value, parent.SpeedTarget);
					return parent.SpeedTarget;
				}
				set { m_speedTarget = value; }
			}

			/// <summary>
			/// <para>The maximum speed relative to the target.</para>
			/// <para>Get returns the lowest value at this or higher level</para>
			/// </summary>
			public float SpeedMaxRelative
			{
				get
				{
					if (parent == null)
						return m_speedMaxRelative.Value;
					if (m_speedMaxRelative.HasValue)
						return Math.Min(m_speedMaxRelative.Value, parent.SpeedMaxRelative);
					return parent.SpeedMaxRelative;
				}
				set { m_speedMaxRelative = value; }
			}

			/// <summary>Pathfinder should not run voxel tests.</summary>
			public bool IgnoreAsteroid
			{
				get { return m_ignoreAsteroid ?? parent.IgnoreAsteroid; }
				set { m_ignoreAsteroid = value; }
			}

			/// <summary>Pathfinder uses this to track when OnTaskComplete_NavWay() is invoked.</summary>
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

			public bool PathfinderCanChangeCourse
			{
				get { return m_pathfindeCanChangeCourse ?? parent.PathfinderCanChangeCourse; }
				set { m_pathfindeCanChangeCourse = value; }
			}

			public bool Stay_In_Formation
			{
				get { return m_formation ?? parent.Stay_In_Formation; }
				set { m_formation = value; }
			}

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
