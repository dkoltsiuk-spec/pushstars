using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Immutable orientation of one captured sensor image. Raw points use the top-left of the
    /// uncorrected texture preview; detector points use the preprocessed MediaPipe input image.
    /// Sensor corrections precede upright rotation. Selfie reflection follows that rotation.
    /// </summary>
    public readonly struct CameraFrameOrientation
    {
        public int RotationDegrees { get; }
        public int SourceWidth { get; }
        public int SourceHeight { get; }
        public bool ReadbackFlipHorizontally { get; }
        public bool ReadbackFlipVertically { get; }
        public bool RawFlipHorizontally => ReadbackFlipHorizontally;
        // Unity pixels are bottom-up; MediaPipe input rows are top-down. That storage conversion
        // is not a visual mirror, so the raw display correction is the inverse readback-V flag.
        public bool RawFlipVertically => !ReadbackFlipVertically;
        public bool IsValid => SourceWidth > 0 && SourceHeight > 0;
        public bool IsQuarterTurn => RotationDegrees == 90 || RotationDegrees == 270;
        public float UprightAspect => !IsValid ? 1f : IsQuarterTurn
            ? (float)SourceHeight / SourceWidth : (float)SourceWidth / SourceHeight;

        public CameraFrameOrientation(int rotationDegrees, bool sourceFlipHorizontally,
            bool sourceFlipVertically, bool videoVerticallyMirrored, int sourceWidth, int sourceHeight)
        {
            int normalized = ((rotationDegrees % 360) + 360) % 360;
            RotationDegrees = (((normalized + 45) / 90) * 90) % 360;
            SourceWidth = sourceWidth > 0 ? sourceWidth : 1;
            SourceHeight = sourceHeight > 0 ? sourceHeight : 1;
            ReadbackFlipHorizontally = sourceFlipHorizontally;
            ReadbackFlipVertically = sourceFlipVertically ^ videoVerticallyMirrored;
        }

        /// <summary>Rotate an already preprocessed detector image point into upright image space.</summary>
        public Vector2 RotateImage(Vector2 point)
        {
            switch (RotationDegrees)
            {
                case 90: return new Vector2(1f - point.y, point.x);
                case 180: return new Vector2(1f - point.x, 1f - point.y);
                case 270: return new Vector2(point.y, 1f - point.x);
                default: return point;
            }
        }

        /// <summary>The same rotation for hip-centered metric points; depth is unchanged.</summary>
        public Vector3 RotateWorld(Vector3 point)
        {
            switch (RotationDegrees)
            {
                case 90: return new Vector3(-point.y, point.x, point.z);
                case 180: return new Vector3(-point.x, -point.y, point.z);
                case 270: return new Vector3(point.y, -point.x, point.z);
                default: return point;
            }
        }

        public Vector2 RawToUpright(Vector2 point)
        {
            if (RawFlipHorizontally) point.x = 1f - point.x;
            if (RawFlipVertically) point.y = 1f - point.y;
            return RotateImage(point);
        }

        public Vector2 UprightToRaw(Vector2 point)
        {
            switch (RotationDegrees)
            {
                case 90: point = new Vector2(point.y, 1f - point.x); break;
                case 180: point = new Vector2(1f - point.x, 1f - point.y); break;
                case 270: point = new Vector2(1f - point.y, point.x); break;
            }
            if (RawFlipHorizontally) point.x = 1f - point.x;
            if (RawFlipVertically) point.y = 1f - point.y;
            return point;
        }

        public Vector2 RawToDisplay(Vector2 point, bool mirror)
            => UprightToDisplay(RawToUpright(point), mirror);

        public Vector2 UprightToDisplay(Vector2 point, bool mirror)
            => new Vector2(mirror ? 1f - point.x : point.x, point.y);

        public Vector2 DisplayToRaw(Vector2 point, bool mirror)
            => UprightToRaw(new Vector2(mirror ? 1f - point.x : point.x, point.y));

        public bool Matches(CameraFrameOrientation other)
            => RotationDegrees == other.RotationDegrees && SourceWidth == other.SourceWidth
                && SourceHeight == other.SourceHeight
                && ReadbackFlipHorizontally == other.ReadbackFlipHorizontally
                && ReadbackFlipVertically == other.ReadbackFlipVertically;
    }

    /// <summary>Optional camera-feed metadata, kept separate from feeds that have no pose source.</summary>
    public interface ICameraFrameOrientationProvider
    {
        bool TryGetCameraOrientation(out CameraFrameOrientation orientation);
        bool TryGetPoseOrientation(float timestampSec, out CameraFrameOrientation orientation);
    }
}
