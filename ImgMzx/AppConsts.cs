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

    public const int MaxImportFiles = 10;
    public const int HashLength = 16;
    public const int VectorSize = 1024;
    public const float HistoryPenalty = 0.2f;
    public const float MaxClusterDistance = 0.75f;

    public const double HistoryPenaltyRate = 0.25;
    public const double PickPower = 8.0;

    // UpdateAge neighbor falloff: increment = 1 / (1 + (rank / AgeRankScale)^2), where rank
    // is the neighbor's 0-based position in distance order (0 = the subject itself -> +1.0),
    // not the raw distance - distance alone is unreliable (near-duplicates can measure as
    // far as 0.56 apart). AgeRankScale=100 puts rank 1000 at ~0.0099, matching the old
    // "increment rounds below 0.01" cutoff, now driven by AgeMaxUpdates instead of a radius.
    public const float AgeRankScale = 100.0f;

    // Dense clusters can still exceed AgeNeighborRadius's typical neighbor count; cap the
    // per-call update batch so one UpdateAge() never turns into a multi-thousand-row write.
    public const int AgeMaxUpdates = 9000;

    // Once MIN(Age) across the table exceeds this, UpdateAge subtracts it from every row so
    // Age stays bounded near zero instead of drifting upward forever (PickNextSubject only
    // cares about relative order, which a uniform shift preserves).
    public const float AgeNormalizeThreshold = 1.0f;
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
    public const string AttributeAge = "age";

    public const string TableVars = "vars";
    public const string AttributeMaxImages = "maximages";
    public const string AttributeW = "w";
    public const string AttributeB = "b";
    public const string AttributeMu = "mu";
    public const string AttributeSigma = "sigma";

    // Online logistic regression over W/B (kept/deleted) in standardized space
    // z = (v - mu) / sigma - see ImagesScore.cs. ScoreLearningRate is a normalized (NLMS)
    // step: one fit moves that image's own score by ScoreLearningRate * (y - p), so updates
    // fade by themselves once the model agrees with the operator. The bias has its own plain
    // step, since under the normalized step it would barely move. ScoreWeightDecay is L2
    // shrink per fit, keeps W bounded (memory horizon ~1/ScoreWeightDecay fits).
    public const float ScoreLearningRate = 0.1f;
    public const float ScoreBiasLearningRate = 0.01f;
    public const float ScoreWeightDecay = 0.0001f;
    public const float ScoreSigmaFloor = 1e-6f;
}