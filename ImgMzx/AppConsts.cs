﻿namespace ImgMzx
{
    public static class AppConsts
    {
        public const string MzxExtension = "mzx";

        public const string FileDatabase = @"D:\Users\Murad\Spacer\spacer.db";
        public const string FileVit = @"D:\Users\Murad\Spacer\RN50x64-openai-visual.onnx";
        public const string FileVitText = @"D:\Users\Murad\Spacer\clip-text-vit-32-float32-int32.onnx";
        public const string FileVisionEncoder = @"D:\Users\Murad\Spacer\vision_encoder.onnx";
        public const string FileEmbedTokens = @"D:\Users\Murad\Spacer\embed_tokens.onnx";
        public const string FileEncoderModel = @"D:\Users\Murad\Spacer\encoder_model.onnx";
        public const string FileDecoderModel = @"D:\Users\Murad\Spacer\decoder_model.onnx";
        public const string BaseVocabFileName = @"D:\Users\Murad\Spacer\vocab.json";
        public const string AdditionalVocabFileName = @"D:\Users\Murad\Spacer\added_tokens.json";
        public const string MergesFileName = @"D:\Users\Murad\Spacer\merges.txt";

        public const string PathHp = @"D:\Users\Murad\Spacer\chunks";
        public const string PathGbProtected = @"M:\removed";
        public const string PathRawProtected = @"M:\raw";

        public const int MaxImportFiles = 100;
        public const int MaxPairs = 100;

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

        public const string TableVars = "vars";
        public const string AttributeMaxImages = "maximages";

        public const string FileVocab = @"D:\Users\Murad\Spacer\vocab.json";
        public const string FileMerges = @"D:\Users\Murad\Spacer\merges.txt";
        public const string FileTokenizer = @"D:\Users\Murad\Spacer\tokenizer.json";
    }
}