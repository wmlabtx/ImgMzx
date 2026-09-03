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

    public const int MaxImportFiles = 100;
    public const int HashLength = 16;
    public const int VectorSize = 1024;

    // Distance added per history-entry of difference when picking the next image, so a
    // candidate from another cohort is preferred only when nothing closer exists in the
    // subject's own. Measured on the live database: distance to the nearest image has a
    // median of 0.154, and the nearest same-cohort image is never worse than the nearest
    // overall by more than 0.122 - so 0.2 reproduces the old hard cohort filter in every
    // observed case, while still yielding a partner for a cohort of one.
    public const float HistoryPenalty = 0.2f;

    // How many rate = 0 subjects PickNextSubject should show per one rate > 0 subject.
    // The boost is derived from this at pick time rather than being a fixed multiplier,
    // because a fixed one would depend on how many images happen to be rated: with a
    // handful rated it would be nearly invisible, and once hundreds are rated it would
    // crowd out everything else.
    public const long UnratedPerRated = 10;

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

    public const string TableVars = "vars";
    public const string AttributeMaxImages = "maximages";
}