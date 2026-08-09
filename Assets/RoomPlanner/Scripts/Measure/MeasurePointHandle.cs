using UnityEngine;

namespace RoomPlanner.Measure
{
    /// <summary>
    /// Ручка на конце измерения (маркер с коллайдером). По ней можно выделить измерение
    /// для удаления или потащить точку. isEndA = это конец A (иначе B).
    /// </summary>
    public class MeasurePointHandle : MonoBehaviour
    {
        [SerializeField] private bool isEndA;

        public bool IsEndA => isEndA;

        private Measurement _owner;
        public Measurement Owner
        {
            get
            {
                if (_owner == null) _owner = GetComponentInParent<Measurement>();
                return _owner;
            }
        }
    }
}
