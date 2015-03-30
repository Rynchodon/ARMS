#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;

namespace Rynchodon
{
	/// <summary>
	/// Makes sure a MyGameLogicComponent gets the right updates. Can only use UpdateAfterSimulation / 10 / 100. DelayedInit will be delayed by 100 updates. 
	/// UpdateAfterSimulation might get called before DelayedUpdate, so check IsInitialized. Will set MyObjectBuilder variable.
	/// </summary>
	public abstract class UpdateEnforcer : MyGameLogicComponent
	{
		private MyEntityUpdateEnum value_EnforcedUpdate = MyEntityUpdateEnum.NONE;
		/// <summary>
		/// do not set to MyEntityUpdateEnum.BEFORE_NEXT_FRAME, not implemented
		/// </summary>
		protected MyEntityUpdateEnum EnforcedUpdate
		{
			get { return value_EnforcedUpdate; }
			set
			{
				if (value == MyEntityUpdateEnum.BEFORE_NEXT_FRAME)
					value = MyEntityUpdateEnum.EACH_FRAME;
				value_EnforcedUpdate = value;
				Entity.NeedsUpdate |= value;
			}
		}

		public override sealed void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			MyObjectBuilder = objectBuilder;
			EnforcedUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
			if (needsToInit == null)
				needsToInit = new LinkedList<UpdateEnforcer>();
			needsToInit.AddLast(this);
			myLogger = new Logger(null, "UpdateEnforcer");
			//myLogger.debugLog("queuing for init: " + Entity.getBestName(), "Init()");
		}

		public override void Close()
		{
			base.Close();
			Closed = true; // possibly set by base.Close()
		}

		/// <summary>
		/// Will be true only after DelayedInit has been called.
		/// </summary>
		protected bool IsInitialized = false;
		protected abstract void DelayedInit();

		private byte updateCount = 0;

		public override void UpdateAfterSimulation()
		{
			if (!IsInitialized) return;
			switch (EnforcedUpdate)
			{
				case MyEntityUpdateEnum.EACH_FRAME:
					//alwaysLog("Method should be overriden", "UpdateAfterSimulation10()", Logger.severity.ERROR);
					return;
				case MyEntityUpdateEnum.EACH_10TH_FRAME:
					updateCount++;
					if (updateCount >= 10)
					{
						updateCount = 0;
						UpdateAfterSimulation10();
					}
					return;
				case MyEntityUpdateEnum.EACH_100TH_FRAME:
					updateCount++;
					if (updateCount >= 100)
					{
						updateCount = 0;
						UpdateAfterSimulation100();
					}
					return;
			}
		}

		public override void UpdateAfterSimulation10()
		{
			if (!IsInitialized) return;
			switch (EnforcedUpdate)
			{
				case MyEntityUpdateEnum.EACH_FRAME:
					if (!Entity.NeedsUpdate.HasFlag(EnforcedUpdate))
					{
						alwaysLog("Entity.NeedsUpdate set to " + Entity.NeedsUpdate + ", should be " + EnforcedUpdate, "UpdateAfterSimulation10()", Logger.severity.WARNING);
						Entity.NeedsUpdate |= EnforcedUpdate;
					}
					UpdateAfterSimulation();
					return;
				case MyEntityUpdateEnum.EACH_10TH_FRAME:
					//alwaysLog("Method should be overriden", "UpdateAfterSimulation10()", Logger.severity.ERROR);
					return;
				case MyEntityUpdateEnum.EACH_100TH_FRAME:
					updateCount += 10;
					if (updateCount >= 100)
					{
						updateCount = 0;
						UpdateAfterSimulation100();
					}
					return;
			}
		}

		public override void UpdateAfterSimulation100()
		{
			if (!IsInitialized) return;
			switch (EnforcedUpdate)
			{
				case MyEntityUpdateEnum.EACH_FRAME:
					if (!Entity.NeedsUpdate.HasFlag(EnforcedUpdate))
					{
						alwaysLog("Entity.NeedsUpdate set to " + Entity.NeedsUpdate + ", should be " + EnforcedUpdate, "UpdateAfterSimulation100()", Logger.severity.WARNING);
						Entity.NeedsUpdate |= EnforcedUpdate;
					}
					UpdateAfterSimulation();
					return;
				case MyEntityUpdateEnum.EACH_10TH_FRAME:
					if (!Entity.NeedsUpdate.HasFlag(EnforcedUpdate))
					{
						alwaysLog("Entity.NeedsUpdate set to " + Entity.NeedsUpdate + ", should be " + EnforcedUpdate, "UpdateAfterSimulation100()", Logger.severity.WARNING);
						Entity.NeedsUpdate |= EnforcedUpdate;
					}
					UpdateAfterSimulation10();
					return;
				case MyEntityUpdateEnum.EACH_100TH_FRAME:
					//alwaysLog("Method should be overriden", "UpdateAfterSimulation100()", Logger.severity.ERROR);
					return;
			}
		}

		/// <summary>
		/// does not seem to fire
		/// </summary>
		public override void UpdatingStopped()
		{
			//if (!IsInitialized) return;
			if (EnforcedUpdate != MyEntityUpdateEnum.NONE)
			{
				alwaysLog("Entity.NeedsUpdate set to " + Entity.NeedsUpdate + ", should be " + EnforcedUpdate, "UpdateAfterSimulation100()", Logger.severity.WARNING);
				Entity.NeedsUpdate |= EnforcedUpdate;
				UpdateAfterSimulation();
				return;
			}
		}

		protected MyObjectBuilder_EntityBase MyObjectBuilder;

		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
		{
			if (MyObjectBuilder == null)
				return null;
			if (copy)
				return MyObjectBuilder.Clone() as MyObjectBuilder_EntityBase;
			return MyObjectBuilder;
		}

		public override sealed void UpdateBeforeSimulation()
		{
			if (IsInitialized)
				return;
			updateCount++;
			if (updateCount < 100)
				return;
			updateCount = 0;
			UpdateBeforeSimulation100();
		}
		public override sealed void UpdateBeforeSimulation10()
		{
			if (IsInitialized)
				return;
			updateCount += 10;
			if (updateCount < 100)
				return;
			updateCount = 0;
			UpdateBeforeSimulation100();
		}
		public override sealed void UpdateBeforeSimulation100()
		{
			if (needsToInit.Count > 0)
				startStalled();
			if (IsInitialized)
				return;
			//(new Logger(null, "UpdateEnforcer")).log("running DelayedInit()", "UpdateBeforeSimulation100()", Logger.severity.TRACE);
			needsToInit.Remove(this);
			EnforcedUpdate = MyEntityUpdateEnum.NONE;
			DelayedInit();
			IsInitialized = true;
		}

		private static LinkedList<UpdateEnforcer> needsToInit;

		/// <summary>
		/// For some blocks, Entity.NeedsUpdate will be forced to None by S.E., this method is used to restart the uninitialized.
		/// </summary>
		private static void startStalled()
		{
			foreach (UpdateEnforcer UE in needsToInit)
				if (UE.Entity.NeedsUpdate == MyEntityUpdateEnum.NONE)
					UE.EnforcedUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
		}

		// needs a logging method for warnings and errors
		private Logger myLogger;
		protected virtual void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(null, "UpdateEnforcer");
			myLogger.log(toLog, method, level);
		}
	}
}
