using System;

namespace Opus.Games.OrchardReach
{
    /// <summary>Position in calibration space, meters, +y up +z forward (matches kinematics-chunk.schema.json's
    /// coordinate convention), plus the normalized workspace coordinates it was placed at.</summary>
    public readonly struct PlacedTarget
    {
        public readonly double X, Y, Z;
        public readonly double AzimuthDeg, ElevationDeg, ReachPercent;

        public PlacedTarget(double x, double y, double z, double azimuthDeg, double elevationDeg, double reachPercent)
        {
            X = x; Y = y; Z = z;
            AzimuthDeg = azimuthDeg; ElevationDeg = elevationDeg; ReachPercent = reachPercent;
        }

        public double[] ToArray() => new[] { X, Y, Z };
    }

    /// <summary>
    /// Pure math: maps a target's normalized workspace coordinates (azimuth off forward, elevation off horizontal,
    /// reach as % of arm length) to a 3D position relative to the seated chest reference established at calibration.
    /// Kept free of UnityEngine/XR types so it is testable with plain doubles (ARCHITECTURE.md §4 workspace grid).
    /// Convention: azimuth 0 = straight ahead, positive = toward the patient's right; elevation 0 = shoulder height
    /// horizontal plane, positive = up.
    /// </summary>
    public static class TargetPlacement
    {
        public static PlacedTarget Place(double[] chestReference, double armLengthM, double azimuthDeg, double elevationDeg, double reachPercent)
        {
            if (chestReference == null || chestReference.Length != 3)
                throw new ArgumentException("chestReference must be a 3-element [x,y,z] array");
            if (armLengthM <= 0) throw new ArgumentOutOfRangeException(nameof(armLengthM), "arm length must be positive");

            double azRad = azimuthDeg * Math.PI / 180.0;
            double elRad = elevationDeg * Math.PI / 180.0;
            double reachM = (reachPercent / 100.0) * armLengthM;

            double dx = Math.Sin(azRad) * Math.Cos(elRad);
            double dy = Math.Sin(elRad);
            double dz = Math.Cos(azRad) * Math.Cos(elRad);

            return new PlacedTarget(
                chestReference[0] + reachM * dx,
                chestReference[1] + reachM * dy,
                chestReference[2] + reachM * dz,
                azimuthDeg, elevationDeg, reachPercent);
        }

        /// <summary>Straight-line distance from the chest reference — used to sanity-check that a placement never
        /// exceeds the patient's calibrated reach envelope even after clamping.</summary>
        public static double DistanceFromChest(double[] chestReference, PlacedTarget target)
        {
            double dx = target.X - chestReference[0];
            double dy = target.Y - chestReference[1];
            double dz = target.Z - chestReference[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
