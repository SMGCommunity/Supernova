using System.Numerics;

namespace SMGEditor.Core.Gravity;

public sealed class GravityZoneSet(IEnumerable<GravityGenerator> generators)
{
    private readonly List<GravityGenerator> _generatorsByPriorityDesc = generators.OrderByDescending(g => g.Priority).ToList();

    public bool TryCalcTotalGravity(Vector3 position, out Vector3 gravityDirection)
    {
        Vector3 total = Vector3.Zero;
        bool hasHit = false;
        int largestPriority = int.MinValue;

        foreach (GravityGenerator generator in _generatorsByPriorityDesc)
        {
            if (hasHit && generator.Priority < largestPriority)
            {
                break;
            }

            if (!generator.TryCalcGravity(position, out Vector3 gravity))
            {
                continue;
            }

            if (hasHit && generator.Priority == largestPriority)
            {
                total += gravity;
            }
            else
            {
                total = gravity;
                largestPriority = generator.Priority;
            }

            hasHit = true;
        }

        gravityDirection = hasHit ? GravityMath.NormalizeOrZero(total) : Vector3.Zero;
        return hasHit;
    }
}
