#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

//using Sandbox.Common;
//using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
//using Sandbox.Definitions;
//using Sandbox.Engine;
//using Sandbox.Game;
using Sandbox.ModAPI;
//using Sandbox.ModAPI.Ingame;
//using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	internal class Collision
	{
		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private static void log(Logger logger, string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			logger.log(level, method, toLog);
		}

		//private Vector3 start, end;

		//private Sandbox.ModAPI.IMyCubeGrid myGrid;
		//private Sandbox.ModAPI.IMyCubeBlock myRC;

		private GridDimensions myGridDim;

		public Collision(GridDimensions gridDim)
		{
			if (gridDim.myRC == null)
				(new Logger("*missing*", "Collision")).log(Logger.severity.FATAL, "..ctor", "ArgumentNullException at Collision()");
			myLogger = new Logger(gridDim.myRC.CubeGrid.DisplayName, "Collision");
			//myGrid = remoteControl.CubeGrid;
			//myRC = remoteControl;
			myGridDim = gridDim;
		}

		private static string getEntityName(IMyEntity entity)
		{
			if (entity == null)
				return null;
			string name = entity.DisplayName;
			if (string.IsNullOrEmpty(name))
			{
				name = entity.Name;
				if (string.IsNullOrEmpty(name))
				{
					name = entity.GetFriendlyName();
					if (string.IsNullOrEmpty(name))
					{
						name = "unknown";
					}
				}
			}
			MyObjectBuilder_EntityBase builder = entity.GetObjectBuilder();
			if (builder != null)
				name += "." + builder.TypeId;
			else
				name += "." + entity.EntityId;
			return name;
		}

		private NavSettings CNS;
		internal long nextUpdate = 0;
		private CollisionAvoidance myCA;

		public collisionAvoidResult avoidCollisions(ref NavSettings CNS, long updateCount, byte tryCount = 10)
		{
			//log("entered avoidCollisions update: "+updateCount);
			if (updateCount < nextUpdate)
				return collisionAvoidResult.NOT_PERFORMED;
			if (myCA == null || nextUpdate != updateCount)
			{
				//log("update should be " + (lastUpdate + 1) + " is " + updateCount);
				this.CNS = CNS;
				myCA = new CollisionAvoidance(ref CNS, myGridDim); //(ref CNS, myRC, collisionLength, distance_from_RC_to_front, lengthOfGrid);
			}
			collisionAvoidResult result;
			if (!myCA.next(out result))
			{
				// will end up here after NO_COLLISION
				if (tryCount <= 0)
				{
					myLogger.log(Logger.severity.ERROR, "avoidCollisions", "Error: too many tries", "avoidCollisions(CNS, "+updateCount+", "+tryCount+")");
					nextUpdate = updateCount + 10;
					return collisionAvoidResult.NO_WAY_FORWARD;
				}
				myCA = null;
				return avoidCollisions(ref CNS, updateCount, (byte)(tryCount - 1));
			}
			if (result == collisionAvoidResult.NO_WAY_FORWARD)
			{
				nextUpdate = updateCount + 10;
				myCA = null;
			}
			else
				nextUpdate = updateCount + 1;
			//log("time to avoidCollisions: " + (DateTime.UtcNow - startOfMethod).TotalMilliseconds+" spheres checked: "+spheresChecked);
			return result;
		}

		public enum collisionAvoidResult : byte { NO_OBSTRUCTION, OBSTRUCTION, ALTERNATE_PATH, NO_WAY_FORWARD, NOT_FINISHED, NOT_PERFORMED }

		/// <summary>
		/// reset on collisionAvoidance.next(), so only applies to current CollisionAvoidance
		/// </summary>
		private static int spheresChecked = 0;
		private static int maxSpheres = 100;
		
		private class CollisionAvoidance
		{
			private Logger myLogger;

			/// <summary>
			/// only use this to push new waypoint, use inital values otherwise
			/// </summary>
			private NavSettings CNS;
			private Vector3D wayDest;
			private Sandbox.ModAPI.IMyCubeGrid destGrid;
			private GridDimensions myGridDims;
			private bool ignoreAsteroids;

			/// <summary>
			/// be sure to get a new one after a reset, CNS changes will not be respected
			/// </summary>
			/// <param name="stopFromDestGrid">how close to destination grid to stop, if destination is a grid</param>
			/// <returns></returns>
			public CollisionAvoidance(ref NavSettings CNS, GridDimensions gridDims)
			{
				VRage.Exceptions.ThrowIf<ArgumentNullException>(CNS == null, "CNS");
				VRage.Exceptions.ThrowIf<ArgumentNullException>(gridDims == null, "gridDims");

				this.CNS = CNS;
				this.wayDest = (Vector3D)CNS.getWayDest();
				this.myGridDims = gridDims;
				this.ignoreAsteroids = CNS.ignoreAsteroids;
				myLogger = new Logger(gridDims.myGrid.DisplayName, "CollisionAvoidance");

				// decide whether to use collision avoidance or slowdown
				switch (CNS.getTypeOfWayDest())
				{
					case NavSettings.TypeOfWayDest.BLOCK:
					case NavSettings.TypeOfWayDest.GRID:
						// run slowdown. see Navigator.calcMoveAndRotate()
						this.destGrid = CNS.CurrentGridDest.Grid;
						break;
					case NavSettings.TypeOfWayDest.LAND:
					default:
						if (CNS.landingState != NavSettings.LANDING.OFF && CNS.CurrentGridDest != null)
							this.destGrid = CNS.CurrentGridDest.Grid;
						break;
				}

				log(myLogger, "created CollisionAvoidance", ".ctor()", Logger.severity.TRACE);
				currentStage = stage.S0_start;
			}

			private enum stage : byte { S0_start, S1_straight, S2_setupAlter, S30_checkAlt0, S31_checkAlt1, S32_checkAlt2, S33_checkAlt3, S100_done }
			private stage currentStage;
			private bool setupAltRoute;
			private CanFlyTo currentCFT;
			private Vector3D collisionSphere;
			private Vector3D routeVector;
			private Vector3D[] altRoute;
			private int multiplier;
			private Vector3D waypoint;

			private void advanceStage()
			{
				if (currentStage == stage.S33_checkAlt3)
				{
					multiplier *= 2;
					currentStage = stage.S30_checkAlt0;
				}
				else
					currentStage++;
			}

			public bool next(out collisionAvoidResult result)
			{
				if (currentStage == stage.S100_done)
				{
					result = collisionAvoidResult.NO_WAY_FORWARD;
					//log("cannot proceed, stage == done");
					return false;
				}
				spheresChecked = 0;
				//log("started next(result) stage: "+currentStage);

				if (currentStage == stage.S0_start)
				{
					//log("building new CanFlyTo("+wayDest+", "+gridDestination+", "+remoteControl+", "+collisionLength+", "+stopFromDestGrid+")");
					currentCFT = new CanFlyTo(wayDest, destGrid, myGridDims, false, ignoreAsteroids); //new CanFlyTo(wayDest, gridDestination, remoteControl, straightRadius, 0, distance_from_RC_to_front);
					advanceStage();
				}

				if (currentStage == stage.S1_straight)
					if (currentCFT.next(out result))
					{
						switch (result)
						{
							case collisionAvoidResult.OBSTRUCTION:
								// find new path
								collisionSphere = currentCFT.relevantSphere.Center;
								advanceStage();
								break;
							case collisionAvoidResult.NOT_FINISHED:
								return true;
							case collisionAvoidResult.NO_OBSTRUCTION:
								currentStage = stage.S100_done;
								log(myLogger, "OK to fly straight ahead");
								return true;
							default:
								myLogger.log(Logger.severity.ERROR, "next()", "Error: unsuitable case from canFlyTo.next(): " + result);
								result = collisionAvoidResult.NO_WAY_FORWARD;
								return false;
						}
					}
					else
					{
						myLogger.log(Logger.severity.ERROR, "next()", "Error at Collision.Avoidance.next()." + currentStage + ": CanFlyTo.next() is invalid");
						currentStage = stage.S100_done;
						return false;
					}

				log(myLogger, "searching for alternate route, spheres so far: "+spheresChecked);

				if (currentStage == stage.S2_setupAlter)
				{
					routeVector = wayDest - myGridDims.getRCworld();
					altRoute = new Vector3D[4];
					altRoute[0] = Vector3D.CalculatePerpendicularVector(routeVector);
					altRoute[1] = routeVector.Cross(altRoute[0]);
					altRoute[2] = Vector3D.Negate(altRoute[0]);
					altRoute[3] = Vector3D.Negate(altRoute[1]);
					setupAltRoute = true;
					multiplier = 10;
					advanceStage();
				}

				while (multiplier < 2000)
				{
					if (setupAltRoute)
					{
						//log("setupAtRoute: "+currentStage+", spheres so far: "+spheresChecked);
						Vector3D direction;
						switch (currentStage)
						{
							case stage.S30_checkAlt0:
								direction = altRoute[0];
								break;
							case stage.S31_checkAlt1:
								direction = altRoute[1];
								break;
							case stage.S32_checkAlt2:
								direction = altRoute[2];
								break;
							case stage.S33_checkAlt3:
								direction = altRoute[3];
								break;
							default:
								direction = new Vector3D();
								break;
						}

						waypoint = collisionSphere + multiplier * Vector3D.Normalize(direction);
						double distance = myGridDims.myGrid.WorldAABB.Distance(waypoint);
						if (distance < CNS.destinationRadius)
						{
							log(myLogger, "throwing out waypoint, too close", "next()", Logger.severity.DEBUG);
							advanceStage();
							continue;
						}
						currentCFT = new CanFlyTo(waypoint, destGrid, myGridDims, true, ignoreAsteroids);
						//currentCFT = new CanFlyTo(waypoint, gridDestination, remoteControl, alternateRadius, alternateProjection, distance_from_RC_to_front);
						setupAltRoute = false;
					}

					if (!currentCFT.next(out result))
					{
						myLogger.log(Logger.severity.ERROR, "next()", "Error at next()." + currentStage + ": CanFlyTo.next() is invalid");
						currentStage = stage.S100_done;
						return false;
					}

					//log("checking result from canFlyTo: "+result);
					switch (result)
					{
						case collisionAvoidResult.OBSTRUCTION:
							setupAltRoute = true;
							//string toLog = "at obstruction, stage "+currentStage;
							advanceStage();
							//log(toLog + " => " + currentStage);
							log(myLogger, "obstruction in this alt path", "next()", Logger.severity.DEBUG);
							break;
						case collisionAvoidResult.NOT_FINISHED:
							log(myLogger, "not finished stage: " + currentStage + ", spheres: " + spheresChecked);
							return true;
						case collisionAvoidResult.NO_OBSTRUCTION:
							if (CNS.addWaypoint(waypoint))
							{
								log(myLogger, "added new waypoint " + waypoint, "next()", Logger.severity.DEBUG);
								result = collisionAvoidResult.ALTERNATE_PATH;
								currentStage = stage.S100_done;
								return true;
							}
							else
								goto case collisionAvoidResult.OBSTRUCTION;
						default:
							myLogger.log(Logger.severity.ERROR, "next()", "Error: unsuitable case from canFlyTo.next(): " + result);
							result = collisionAvoidResult.NO_WAY_FORWARD;
							return false;
					}
				}

				//log("Warning: no way forward");// + ", checked " + spheresChecked + " spheres");
				result = collisionAvoidResult.NO_WAY_FORWARD;
				return true;
			}
		}

		public class CanFlyTo
		{
			private Logger myLogger;

			/// <summary>
			/// only set when collisionAvoidResult == OBSTRUCTION
			/// </summary>
			public BoundingSphereD relevantSphere { get; private set; }

			private Sandbox.ModAPI.IMyCubeGrid myGrid;
			private Sandbox.ModAPI.IMyCubeGrid gridDest;

			private Spheres collisionSpheres;
			private bool isValid = true;
			private AttachedGrids myAttached;
			private bool ignoreAsteroids;

			public CanFlyTo(Vector3D destination, Sandbox.ModAPI.IMyCubeGrid gridDest, GridDimensions gridDims, bool isAlternate, bool ignoreAsteroids)
			{
				this.myGrid = gridDims.myGrid;
				this.gridDest = gridDest;
				this.myAttached = AttachedGrids.getFor(gridDims.myGrid);
				this.ignoreAsteroids = ignoreAsteroids;
				myLogger = new Logger(gridDims.myGrid.DisplayName, "CanFlyTo");
				collisionSpheres = new Spheres(destination, gridDims, isAlternate);
				log(myLogger, "got grid dest: " + this.gridDest, ".ctor()", Logger.severity.TRACE);
			}

			//public CanFlyTo(IMyCubeGrid grid, BoundingSphereD toCheck)
			//{
			//	this.myGrid = grid;
			//	myLogger = new Logger(grid.DisplayName, "CanFlyTo");
			//	collisionSpheres = new Spheres(toCheck);
			//}

			/// <summary>
			/// 
			/// </summary>
			/// <param name="result"></param>
			/// <returns>false iff could not get a result</returns>
			public bool next(out collisionAvoidResult result)
			{
				if (!isValid)
				{
					result = collisionAvoidResult.NO_WAY_FORWARD;
					myLogger.log(Logger.severity.ERROR, "next()", "cannot proceed, not valid");
					return false;
				}
				isValid = false;
				blocksChecked = 0;

				BoundingSphereD currentSphere;
				while (collisionSpheres.next(out currentSphere))
				{
					//log("got sphere: "+currentSphere);
					if (spheresChecked >= maxSpheres)
					{
						isValid = true;
						result = collisionAvoidResult.NOT_FINISHED;
						return true;
					}
					spheresChecked++;
					relevantSphere = currentSphere;

					List<IMyEntity> entitiesInSphere = MyAPIGateway.Entities.GetEntitiesInSphere(ref currentSphere);
					if (entitiesInSphere.Count > 0)
						if (gridDest != null && entitiesInSphere.Contains(gridDest as IMyEntity))
						{
							result = collisionAvoidResult.NO_OBSTRUCTION;
							log(myLogger, "sphere contains grid dest", "next()", Logger.severity.DEBUG);
							return true;
						}
						else // does not contain gridDest
							foreach (IMyEntity entity in entitiesInSphere)
								if (!ignoreCollision(entity, currentSphere))
								{
									log(myLogger, "obstruction: " + getEntityName(entity));
									result = collisionAvoidResult.OBSTRUCTION;
									return true;
								}
								else if (expensiveTest)
								{ // intersection test is expensive, delay
									log(myLogger, "performed expensiveTest, delaying next sphere");
									isValid = true;
									result = collisionAvoidResult.NOT_FINISHED;
									return true;
								}
				}
				// completed successfully with no obstacles
				result = collisionAvoidResult.NO_OBSTRUCTION;
				return true;
			}

			private bool expensiveTest;

			private bool ignoreCollision(IMyEntity entity, BoundingSphereD collision)
			{
				expensiveTest = false;
				//MyObjectBuilder_EntityBase builder = entity.GetObjectBuilder();
				if (!(entity is IMyVoxelMap || entity is IMyCubeGrid))
					return true;

				//if (builder == null)
				//{
				//	//log(myLogger, "ignoring object: no builder: " + getEntityName(entity));
				//	return true; // do not know what this is but it is not stopping me
				//}
				//if (builder is MyObjectBuilder_Character)
				//{
				//	//log(myLogger, "ignoring object: squish the little shits: " + getEntityName(entity));
				//	return true; // squish the little shits
				//}
				if (entity == myGrid as IMyEntity)
				{
					//log(myLogger, "ignoring object: that's me: " + getEntityName(entity));
					return true; // cannot run self over
				}
				IMyCubeGrid entityAsGrid = entity as IMyCubeGrid;
				if (entity.Physics != null && entity.Physics.Mass > 0 && entity.Physics.Mass < 1000)
				{
					log(myLogger, "ignoring object: low mass(" + entity.Physics.Mass + "): " + getEntityName(entity));
					return true; // low mass object
				}
				//log(myLogger, "checking attached: " + getEntityName(entity), "ignoreCollision()", Logger.severity.TRACE);
				if (entityAsGrid != null && myAttached.isGridAttached(entityAsGrid))
				{
					log(myLogger, "ignoring object: attached: " + getEntityName(entity), "ignoreCollision()", Logger.severity.TRACE);
					return true;
				}
				log(myLogger, "comparing " + getEntityName(entityAsGrid) + " to " + getEntityName(gridDest) + " for is grid dest", "ignoreCollision()", Logger.severity.TRACE);
				if (entityAsGrid != null && gridDest != null && entityAsGrid == gridDest)
				{
					log(myLogger, "ignoring object: dest grid " + getEntityName(entity) + ", " + collision + ", blocks checked=" + blocksChecked);
					return true;
				}
				if (!entityIntersectsWithSphere(entity, collision))
				{
					log(myLogger, "ignoring object: no intersection " + getEntityName(entity) + ", " + collision + ", blocks checked=" + blocksChecked);
					return true;
				}
				log(myLogger, "sphere intersects object " + getEntityName(entity) + ", " + collision + ", blocks checked=" + blocksChecked);
				//if (entityAsGrid != null)
				//	log(myLogger, "grid is trash = " + entityAsGrid.IsTrash() + ", mass = " + entityAsGrid.Physics.Mass, "ignoreCollision()", Logger.severity.TRACE);
				return false;
			}

			private bool entityIntersectsWithSphere(IMyEntity entity, BoundingSphereD sphere)
			{
				IMyCubeGrid grid = entity as IMyCubeGrid;
				if (grid == null)
				{
					log(myLogger, "Asteroid: " + entity.getBestName() + ", " + !ignoreAsteroids, "entityIntersectsWithSphere()", Logger.severity.DEBUG);

					return !ignoreAsteroids;
					//return entity.GetIntersectionWithSphere(ref sphere); // not at all reliable
				}
				expensiveTest = true;
				log(myLogger, "using grid test", "entityIntersectsWithSphere()", Logger.severity.DEBUG);

				List<IMySlimBlock> blocksInGrid = new List<IMySlimBlock>();
				grid.GetBlocks(blocksInGrid);
				BoundingSphere sphereF = sphere;
				DateTime beforeBlockCheck = DateTime.UtcNow;
				foreach (IMySlimBlock block in blocksInGrid)
				{
					blocksChecked++;
					Vector3 worldPos = grid.GridIntegerToWorld(block.Position);
					if (sphereF.Contains(worldPos) != ContainmentType.Disjoint)
					{
						//log(myLogger, "took " + (DateTime.UtcNow - beforeBlockCheck).TotalMilliseconds + " to blockCheck, result=true");
						return true;
					}
				}
				//log(myLogger, "took " + (DateTime.UtcNow - beforeBlockCheck).TotalMilliseconds + " to blockCheck, result=false");
				return false; // nothing found*/
			}

			private int blocksChecked;

			//private BoundingBoxD getCollisionBox(bool sidel = false)
			//{
			//	LinkedList<Vector3D> points = new LinkedList<Vector3D>();
			//	// build rectangle cross section of WorldAABB (different for sidel)
			//	if (sidel)
			//	{
			//		// NYI
			//	}
			//	else // not sidel
			//	{
			//		Vector3 centre = myGrid.WorldAABB.Center
			//		RelativeVector3F first = RelativeVector3F.createFromGrid(
			//	}

			//	// for destination rectangle project past destination (say length of ship for simplicity)
			//	// construct BoundingBoxD from points
			//	return BoundingBoxD.CreateFromPoints(points);
			//}
		}

		/// <summary>
		/// iterates over a series of BoundingSphereD between two points
		/// </summary>
		private class Spheres
		{
			private Logger myLogger;

			private Vector3D start;
			private Vector3D end;
			/// <summary>
			/// vector in the direction start to end, with magnitude of diameter
			/// </summary>
			private Vector3D startToEnd;
			private float radius;

			private static int startAtSphere = 1;
			private int maxSphereNum;
			private int sphereNum;

			public Spheres(Vector3D end, GridDimensions gridDims, bool isAlternate)
			{
				myLogger = new Logger(gridDims.myGrid.DisplayName, "Spheres");
				sphereNum = startAtSphere;

				this.radius = Math.Max(gridDims.width, gridDims.height) / 2;
				float projection = 0;
				if (isAlternate)
				{
					radius = radius * 3f + 10f; // alternate buffer
					projection = gridDims.length * 5; // alternate projection
				}
				else
					radius = radius * 1.5f + 5f; // straight buffer

				this.start = gridDims.myGrid.GetCentre();
				Vector3 RCtoCentre = this.start - gridDims.getRCworld(); // in metres, world distance
				this.end = end + RCtoCentre;
				startToEnd = end - start; // set direction to start -> end. current length is (end-start).length()

				float distance_between = radius / 2;
				maxSphereNum = (int)Math.Ceiling((startToEnd.Length() + projection) / distance_between);

				startToEnd.Normalize(); // length is now 1
				start += startToEnd * gridDims.distance_to_front_from_centre; // otherwise it is much harder to find an alt path
				startToEnd *= distance_between; // length is now distance_between
				
				log(myLogger, "maxSphereNum: " + maxSphereNum + ", distance_between=" + distance_between + ", projection=" + projection, "constructor", Logger.severity.TRACE);
			}

			//private BoundingSphereD special_sphere;

			///// <summary>
			///// dummy
			///// </summary>
			///// <param name="sphere"></param>
			//public Spheres(BoundingSphereD sphere)
			//{
			//	special_sphere = sphere;
			//	sphereNum = 0;
			//}

			/// <summary>
			/// get the next BoundingSphereD in the set
			/// </summary>
			/// <param name="result"></param>
			/// <returns>true iff result is valid</returns>
			public bool next(out BoundingSphereD result)
			{
				//if (special_sphere != null) // this is wrong, BoundingSphereD is not a nullable type
				//{
				//	result = special_sphere;
				//	sphereNum++;
				//	if (sphereNum > 0)
				//		return false;
				//	return true;
				//}

				if (sphereNum > maxSphereNum && sphereNum > startAtSphere)
				{
					result = new BoundingSphereD();
					return false;
				}
				Vector3D centre = start + startToEnd * sphereNum;
				sphereNum++;
				result = new BoundingSphereD(centre, radius);
				log(myLogger, "served sphere: "+result, "next(result)", Logger.severity.TRACE);
				return true;
			}
		}

		//private class Box
		//{
		//	private Logger myLogger;
		//	private Vector3D start, end;
		//	private Vector3D start_to_end;
		//	private GridDimensions myGridDim;

		//	private BoundingBoxD initialBox;
		//	private int maxBoxNum;
		//	private int curBoxNum;

		//	public Box(Vector3D end, GridDimensions gridDims, bool isAlternate)
		//	{
		//		this.end = end;
		//		this.start = myGridDim.getRCworld();
		//	}

		//	public bool next(out BoundingBoxD result)
		//	{
		//		result = new BoundingBoxD();
		//		return false;
		//	}
		//}
	}
}
