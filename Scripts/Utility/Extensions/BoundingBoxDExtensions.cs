using System.Collections;
using System.Collections.Generic;
using VRageMath;

namespace Rynchodon.Utility.Extensions
{
	public static class BoundingBoxDExtensions
	{

		public static Corners GetCorners(BoundingBoxD box)
		{
			return new Corners(ref box);
		}

		public static Corners GetCorners(ref BoundingBoxD box)
		{
			return new Corners(ref box);
		}

		public struct Corners : IEnumerable<Vector3D>
		{
			private BoundingBoxD _box;

			public Corners(ref BoundingBoxD box)
			{
				_box = box;
			}

			public Enumerator GetEnumerator()
			{
				return new Enumerator(ref _box);
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}

			IEnumerator<Vector3D> IEnumerable<Vector3D>.GetEnumerator()
			{
				return GetEnumerator();
			}

			public struct Enumerator : IEnumerator<Vector3D>
			{
				private readonly BoundingBoxD _box;
				private byte _index;

				/// <summary>Exposed for access by reference.</summary>
				public Vector3D _current;

				public Enumerator(ref BoundingBoxD box)
				{
					_box = box;
					_index = 0;
					_current = default(Vector3D);
				}

				/// <summary>
				/// Use <see cref="_current"/> for ref.
				/// </summary>
				public Vector3D Current
				{
					get { return _current; }
				}

				object IEnumerator.Current
				{
					get { return Current; }
				}

				public void Dispose() { }

				public bool MoveNext()
				{
					switch (_index)
					{
						case 0:
							_current.X = _box.Min.X;
							_current.Y = _box.Max.Y;
							_current.Z = _box.Max.Z;
							++_index;
							return true;
						case 1:
							_current.X = _box.Max.X;
							_current.Y = _box.Max.Y;
							_current.Z = _box.Max.Z;
							++_index;
							return true;
						case 2:
							_current.X = _box.Max.X;
							_current.Y = _box.Min.Y;
							_current.Z = _box.Max.Z;
							++_index;
							return true;
						case 3:
							_current.X = _box.Min.X;
							_current.Y = _box.Min.Y;
							_current.Z = _box.Max.Z;
							++_index;
							return true;
						case 4:
							_current.X = _box.Min.X;
							_current.Y = _box.Max.Y;
							_current.Z = _box.Min.Z;
							++_index;
							return true;
						case 5:
							_current.X = _box.Max.X;
							_current.Y = _box.Max.Y;
							_current.Z = _box.Min.Z;
							++_index;
							return true;
						case 6:
							_current.X = _box.Max.X;
							_current.Y = _box.Min.Y;
							_current.Z = _box.Min.Z;
							++_index;
							return true;
						case 7:
							_current.X = _box.Min.X;
							_current.Y = _box.Min.Y;
							_current.Z = _box.Min.Z;
							++_index;
							return true;
						default:
							return false;
					}
				}

				public void Reset()
				{
					_index = 0;
				}
			}
		}

	}
}
