  using System.Dynamic;

namespace ImgMzx
{
    public static class AppConsts
    {
        public const string MzxExtension = "mzx";

        public const string FileDatabase = @"D:\Users\Murad\Spacer\spacer.db";
        public const string FileVit = @"D:\Users\Murad\Spacer\RN50x64-openai-visual.onnx";

        public const string PathHp = @"D:\Users\Murad\Spacer\chunks";
        public const string PathGbProtected = @"M:\removed";
        public const string PathRawProtected = @"M:\raw";

        public const int MaxImportFiles = 100;
        public const int MaxHistorySize = 100;
        public const int VectorSize = 1024;
        public const int HashLength = 64;

        public const char CharEllipsis = '\u2026';
        public const char CharRightArrow = '\u2192';

        public const int LockTimeout = 10000;
        public const double WindowMargin = 5.0;
        public const double TimeLapse = 500.0;

        public const string TableImages = "images";
        public const string AttributeHash = "hash";
        public const string AttributeName = "name";
        public const string AttributeVector = "vector";
        public const string AttributeRotateMode = "rotatemode";
        public const string AttributeFlipMode = "flipmode";
        public const string AttributeLastView = "lastview";
        public const string AttributeVerified = "verified";
        public const string AttributeDistance = "distance";
        public const string AttributeNext = "next";
        public const string AttributeScore = "score";
        public const string AttributeLastCheck = "lastcheck";
        public const string AttributeHistory = "history";
        public const string AttributeKey = "key";

        public const string TableVars = "vars";
        public const string AttributeMaxImages = "maximages";
    }
}