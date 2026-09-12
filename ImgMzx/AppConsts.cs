namespace ImgMzx;

public static class AppConsts
{
    public const string MzxExtension = "mzx";
    public const string MexExtension = "mex";

    public const string FileDatabase = @"D:\Users\Murad\Spacer\spacer.db";
    public const string FileVit = @"D:\Users\Murad\Spacer\model_q4.onnx";

    public const string PathHp = @"D:\Users\Murad\Spacer\chunks";
    public const string PathDeleted = @"M:\deleted";
    public const string PathHpBackup = @"G:\Spacer\backup";
    public const string PathRawProtected = @"M:\raw";
    public const string PathExport = @"M:\export";

    public const int MaxImportFiles = 1000;
    public const int HashLength = 16;
    public const int VectorSize = 1024;
    public const float HistoryPenalty = 0.2f;

    // How fast the history penalty in PickNextSubject grows: a subject with n history
    // entries is ordered by distance ^ (1 / (1 + HistoryPenaltyRate * n)). Distances are
    // in (0, 1), so the root pushes the value towards 1 and the row falls back in the
    // queue. At 0.25 the penalty is gentle - with distance 0.36: n=1 -> 0.44, n=2 -> 0.51,
    // n=7 -> 0.69. Raise it to 1.0 for the plain 1/(n+1) curve (n=1 -> 0.60, n=7 -> 0.88),
    // set it to 0 to order by raw distance.
    public const double HistoryPenaltyRate = 0.25;
    public const double PickPower = 8.0;
    public const char CharEllipsis = '\u2026';

    // not used
    // public const char CharRightArrow = '\u2192';

    public const double WindowMargin = 5.0;
    public const double TimeLapse = 500.0;

    public const string TableImages = "images";
    public const string AttributeHash = "hash";
    public const string AttributeVector = "vector";
    public const string AttributeRotateMode = "rotatemode";
    public const string AttributeFlipMode = "flipmode";
    public const string AttributeLastView = "lastview";
    public const string AttributeHistory = "history";
    public const string AttributeRate = "rate";
    public const string AttributeDistance = "distance";

    public const string TableVars = "vars";
    public const string AttributeMaxImages = "maximages";
}