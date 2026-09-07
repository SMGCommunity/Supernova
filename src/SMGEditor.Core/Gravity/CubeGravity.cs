using System.Numerics;

namespace SMGEditor.Core.Gravity;

public sealed class CubeGravity(Vector3 translation, Vector3 xDir, Vector3 yDir, Vector3 zDir, byte activeFaces) : GravityGenerator
{
    private const int NegativeX = 0, BetweenX = 1, PositiveX = 2;
    private const int NegativeY = 0, BetweenY = 3, PositiveY = 6;
    private const int NegativeZ = 0, BetweenZ = 9, PositiveZ = 18;

    private const int FaceXPositive = PositiveX + BetweenY + BetweenZ;
    private const int FaceXNegative = NegativeX + BetweenY + BetweenZ;
    private const int FaceYPositive = BetweenX + PositiveY + BetweenZ;
    private const int FaceYNegative = BetweenX + NegativeY + BetweenZ;
    private const int FaceZPositive = BetweenX + BetweenY + PositiveZ;
    private const int FaceZNegative = BetweenX + BetweenY + NegativeZ;

    private readonly float _lenX = xDir.Length();
    private readonly float _lenY = yDir.Length();
    private readonly float _lenZ = zDir.Length();

    private int CalcGravityArea(Vector3 position)
    {
        Vector3 relative = position - translation;
        float xDirDistance = Vector3.Dot(relative, xDir) / _lenX;
        float yDirDistance = Vector3.Dot(relative, yDir) / _lenY;
        float zDirDistance = Vector3.Dot(relative, zDir) / _lenZ;

        int area;
        if (xDirDistance < -_lenX)
        {
            if ((activeFaces & 2) == 0)
            {
                return -1;
            }

            area = NegativeX;
        }
        else if (xDirDistance <= _lenX)
        {
            area = BetweenX;
        }
        else
        {
            if ((activeFaces & 1) == 0)
            {
                return -1;
            }

            area = PositiveX;
        }

        if (yDirDistance < -_lenY)
        {
            if ((activeFaces & 8) == 0)
            {
                return -1;
            }

            area += NegativeY;
        }
        else if (yDirDistance <= _lenY)
        {
            area += BetweenY;
        }
        else
        {
            if ((activeFaces & 4) == 0)
            {
                return -1;
            }

            area += PositiveY;
        }

        if (zDirDistance < -_lenZ)
        {
            if ((activeFaces & 32) == 0)
            {
                return -1;
            }

            area += NegativeZ;
        }
        else if (zDirDistance <= _lenZ)
        {
            area += BetweenZ;
        }
        else
        {
            if ((activeFaces & 16) == 0)
            {
                return -1;
            }

            area += PositiveZ;
        }

        return area;
    }

    private bool CalcFaceGravity(Vector3 position, int area, out Vector3 direction, out float distance)
    {
        Vector3 antiFaceDir;
        switch (area)
        {
            case FaceZNegative:
                antiFaceDir = zDir;
                break;
            case FaceYNegative:
                antiFaceDir = yDir;
                break;
            case FaceXNegative:
                antiFaceDir = xDir;
                break;
            case FaceXPositive:
                antiFaceDir = -xDir;
                break;
            case FaceYPositive:
                antiFaceDir = -yDir;
                break;
            case FaceZPositive:
                antiFaceDir = -zDir;
                break;
            default:
                direction = Vector3.Zero;
                distance = 0f;
                return false;
        }

        GravityMath.SeparateScalarAndDirection(antiFaceDir, out float length, out Vector3 unitAntiFaceDir);
        float height = Vector3.Dot(unitAntiFaceDir, translation - position) - length;
        direction = unitAntiFaceDir;
        distance = MathF.Max(height, 0f);
        return true;
    }

    private bool CalcEdgeGravity(Vector3 position, int area, out Vector3 direction, out float distance)
    {
        const int axisX = 1, axisY = 3, axisZ = 9;

        Vector3 edgeVector;
        Vector3 edgeTranslation;
        switch (area)
        {
            case axisX + NegativeY + NegativeZ:
                edgeVector = xDir;
                edgeTranslation = -yDir - zDir;
                break;
            case axisY + NegativeX + NegativeZ:
                edgeVector = yDir;
                edgeTranslation = -xDir - zDir;
                break;
            case axisY + PositiveX + NegativeZ:
                edgeVector = yDir;
                edgeTranslation = xDir - zDir;
                break;
            case axisX + PositiveY + NegativeZ:
                edgeVector = xDir;
                edgeTranslation = yDir - zDir;
                break;
            case axisZ + NegativeX + NegativeY:
                edgeVector = zDir;
                edgeTranslation = -xDir - yDir;
                break;
            case axisZ + PositiveX + NegativeY:
                edgeVector = zDir;
                edgeTranslation = xDir - yDir;
                break;
            case axisZ + NegativeX + PositiveY:
                edgeVector = zDir;
                edgeTranslation = -xDir + yDir;
                break;
            case axisZ + PositiveX + PositiveY:
                edgeVector = zDir;
                edgeTranslation = xDir + yDir;
                break;
            case axisX + NegativeY + PositiveZ:
                edgeVector = xDir;
                edgeTranslation = -yDir + zDir;
                break;
            case axisY + NegativeX + PositiveZ:
                edgeVector = yDir;
                edgeTranslation = -xDir + zDir;
                break;
            case axisY + PositiveX + PositiveZ:
                edgeVector = yDir;
                edgeTranslation = xDir + zDir;
                break;
            case axisX + PositiveY + PositiveZ:
                edgeVector = xDir;
                edgeTranslation = yDir + zDir;
                break;
            default:
                direction = Vector3.Zero;
                distance = 0f;
                return false;
        }

        edgeTranslation += translation;
        Vector3 unitEdgeVector = GravityMath.NormalizeOrZero(edgeVector);

        Vector3 positionOppositeInOrthogonalPlane = GravityMath.KillElement(edgeTranslation - position, unitEdgeVector);

        if (GravityMath.IsNearZero(positionOppositeInOrthogonalPlane))
        {
            direction = GravityMath.NormalizeOrZero(edgeTranslation - translation);
            distance = 0f;
        }
        else
        {
            GravityMath.SeparateScalarAndDirection(positionOppositeInOrthogonalPlane, out distance, out direction);
        }

        return true;
    }

    private bool CalcCornerGravity(Vector3 position, int area, out Vector3 direction, out float distance)
    {
        Vector3 vertex;
        switch (area)
        {
            case NegativeX + NegativeY + NegativeZ:
                vertex = -xDir - yDir - zDir;
                break;
            case PositiveX + NegativeY + NegativeZ:
                vertex = xDir - yDir - zDir;
                break;
            case NegativeX + PositiveY + NegativeZ:
                vertex = -xDir + yDir - zDir;
                break;
            case PositiveX + PositiveY + NegativeZ:
                vertex = xDir + yDir - zDir;
                break;
            case NegativeX + NegativeY + PositiveZ:
                vertex = -xDir - yDir + zDir;
                break;
            case PositiveX + NegativeY + PositiveZ:
                vertex = xDir - yDir + zDir;
                break;
            case NegativeX + PositiveY + PositiveZ:
                vertex = -xDir + yDir + zDir;
                break;
            case PositiveX + PositiveY + PositiveZ:
                vertex = xDir + yDir + zDir;
                break;
            default:
                direction = Vector3.Zero;
                distance = 0f;
                return false;
        }

        vertex += translation;

        Vector3 gravity = vertex - position;
        if (GravityMath.IsNearZero(gravity))
        {
            distance = 0f;
            direction = GravityMath.NormalizeOrZero(vertex - translation);
        }
        else
        {
            GravityMath.SeparateScalarAndDirection(gravity, out distance, out direction);
        }

        return true;
    }

    protected override bool TryCalcOwnGravity(Vector3 position, out Vector3 direction, out float distance)
    {
        int area = CalcGravityArea(position);
        if (area < 0)
        {
            direction = Vector3.Zero;
            distance = 0f;
            return false;
        }

        if (!CalcFaceGravity(position, area, out direction, out distance) &&
            !CalcEdgeGravity(position, area, out direction, out distance) &&
            !CalcCornerGravity(position, area, out direction, out distance))
        {
            return false;
        }

        return IsInRangeDistance(distance);
    }
}
