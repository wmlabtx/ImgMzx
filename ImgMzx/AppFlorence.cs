using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static ImgMzx.AppFlorence;

namespace ImgMzx;

public enum Florence2TaskType
{
    // Basic captioning
    Caption, // supported by post-processor
    DetailedCaption, // supported by post-processor
    MoreDetailedCaption, // supported by post-processor

    // OCR
    Ocr, // supported by post-processor
    OcrWithRegions, // supported by post-processor

    // Object detection
    ObjectDetection, // supported by post-processor
    DenseRegionCaption, // supported by post-processor
    RegionProposal, // supported by post-processor

    // Region analysis
    RegionToDescription,
    RegionToSegmentation,
    RegionToCategory,
    RegionToOcr,

    // Grounding and detection
    CaptionToGrounding,
    ReferringExpressionSegmentation,
    OpenVocabularyDetection
}

public class Florence2Tasks
{
    public static readonly IReadOnlyDictionary<Florence2TaskType, Florence2Tasks> TaskConfigurations = new Dictionary<Florence2TaskType, Florence2Tasks> {
        // Basic query with text output
        [Florence2TaskType.Caption] = new(Florence2TaskType.Caption, "<CAPTION>", "What does the image describe?", false, false, true, false, false, false),
        [Florence2TaskType.DetailedCaption] = new(Florence2TaskType.DetailedCaption, "<DETAILED_CAPTION>", "Describe in detail what is shown in the image.", false, false, true, false, false, false),
        [Florence2TaskType.MoreDetailedCaption] = new(Florence2TaskType.MoreDetailedCaption, "<MORE_DETAILED_CAPTION>", "Describe with a paragraph what is shown in the image.", false, false, true, false, false, false),
        [Florence2TaskType.Ocr] = new(Florence2TaskType.Ocr, "<OCR>", "What is the text in the image?", false, false, true, false, false, false),

        // Basic query with regions/labels/polygons output 
        [Florence2TaskType.OcrWithRegions] = new(Florence2TaskType.OcrWithRegions, "<OCR_WITH_REGION>", "What is the text in the image, with regions?", false, false, false, true, true, true),

        // Basic query with regions/labels output 
        [Florence2TaskType.ObjectDetection] = new(Florence2TaskType.ObjectDetection, "<OD>", "Locate the objects with category name in the image.", false, false, false, true, true, false),
        [Florence2TaskType.DenseRegionCaption] = new(Florence2TaskType.DenseRegionCaption, "<DENSE_REGION_CAPTION>", "Locate the objects in the image, with their descriptions.", false, false, false, true, true, false),
        [Florence2TaskType.RegionProposal] = new(Florence2TaskType.RegionProposal, "<REGION_PROPOSAL>", "Locate the region proposals in the image.", false, false, false, true, true, false),

        // Grounding and detection
        [Florence2TaskType.CaptionToGrounding] = new(Florence2TaskType.CaptionToGrounding, "<CAPTION_TO_PHRASE_GROUNDING>", "Locate the phrases in the caption: {0}", false, true, false, true, true, false),
        [Florence2TaskType.ReferringExpressionSegmentation] = new(Florence2TaskType.ReferringExpressionSegmentation, "<REFERRING_EXPRESSION_SEGMENTATION>", "Locate {0} in the image with mask", false, true, false, false, false, true),
        [Florence2TaskType.OpenVocabularyDetection] = new(Florence2TaskType.OpenVocabularyDetection, "<OPEN_VOCABULARY_DETECTION>", "Locate {0} in the image.", false, true, false, true, false, true), // not sure yet

        // Region analysis
        [Florence2TaskType.RegionToSegmentation] = new(Florence2TaskType.RegionToSegmentation, "<REGION_TO_SEGMENTATION>", "What is the polygon mask of region {0}", true, false, false, false, false, true), // not sure yet
        [Florence2TaskType.RegionToCategory] = new(Florence2TaskType.RegionToCategory, "<REGION_TO_CATEGORY>", "What is the region {0}?", true, false, true, false, false, false), // not sure yet
        [Florence2TaskType.RegionToDescription] = new(Florence2TaskType.RegionToDescription, "<REGION_TO_DESCRIPTION>", "What does the region {0} describe?", true, false, true, false, false, false), // not sure yet
        [Florence2TaskType.RegionToOcr] = new(Florence2TaskType.RegionToOcr, "<REGION_TO_OCR>", "What text is in the region {0}?", true, false, true, false, false, false) // not sure yet
    };

    private Florence2Tasks(
        Florence2TaskType taskType,
        string promptAlias,
        string prompt,
        bool requiresRegionInput,
        bool requiresSubPrompt,
        bool returnsText,
        bool returnsLabels,
        bool returnsBoundingBoxes,
        bool returnsPolygons)
    {
        TaskType = taskType;
        PromptAlias = promptAlias;
        Prompt = prompt;
        RequiresRegionInput = requiresRegionInput;
        RequiresSubPrompt = requiresSubPrompt;
        ReturnsText = returnsText;
        ReturnsLabels = returnsLabels;
        ReturnsBoundingBoxes = returnsBoundingBoxes;
        ReturnsPolygons = returnsPolygons;
    }

    public Florence2TaskType TaskType { get; }
    public string PromptAlias { get; }
    public string Prompt { get; }
    public bool RequiresRegionInput { get; }
    public bool RequiresSubPrompt { get; }
    public bool ReturnsText { get; }
    public bool ReturnsLabels { get; }
    public bool ReturnsBoundingBoxes { get; }
    public bool ReturnsPolygons { get; }

    /// <summary>
    /// Creates a query for the specified task type.
    /// </summary>
    /// <param name="taskType">
    /// The task type. Supported types are:
    /// - Caption
    /// - DetailedCaption
    /// - MoreDetailedCaption
    /// - Ocr
    /// - OcrWithRegions
    /// - ObjectDetection
    /// - DenseRegionCaption
    /// - RegionProposal
    /// </param>
    /// <returns>
    /// A query for the specified task type.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the task type is not supported.
    /// </exception>
    public static Florence2Query CreateQuery(Florence2TaskType taskType)
    {
        if (!TaskConfigurations.TryGetValue(taskType, out var config))
            throw new ArgumentException($"Unsupported task type: {taskType}");

        if (config.RequiresRegionInput)
            throw new ArgumentException($"Task {taskType} requires region parameter");

        if (config.RequiresSubPrompt)
            throw new ArgumentException($"Task {taskType} requires sub-prompt parameter");

        return new Florence2Query(taskType, config.Prompt);
    }

    /// <summary>
    /// Creates a query for the specified task type with the specified region. 
    /// </summary>
    /// <param name="taskType">
    /// The task type. Supported types are:
    /// - RegionToSegmentation
    /// - RegionToCategory
    /// - RegionToDescription
    /// - RegionToOcr 
    /// </param>
    /// <param name="region">
    /// The region of the image to query.
    /// </param>
    /// <returns>
    /// A query for the specified task type with the specified region.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the task type is not supported.
    /// </exception>
    public static Florence2Query CreateQuery(Florence2TaskType taskType, SixLabors.ImageSharp.RectangleF region)
    {
        if (!TaskConfigurations.TryGetValue(taskType, out var config))
            throw new ArgumentException($"Unsupported task type: {taskType}");

        if (!config.RequiresRegionInput)
            throw new ArgumentException($"Task {taskType} does not handle region parameter");

        var regionString = region.ToString();
        return new Florence2Query(taskType, string.Format(config.Prompt, regionString));
    }

    /// <summary>
    /// Creates a query for the specified task type with the specified sub-prompt. 
    /// </summary>
    /// <param name="taskType">
    /// The task type. Supported types are:
    /// - CaptionToGrounding
    /// - ReferringExpressionSegmentation
    /// - OpenVocabularyDetection 
    /// </param>
    /// <param name="subPrompt">
    /// The sub-prompt to include in the query.
    /// </param>
    /// <returns>
    /// A query for the specified task type with the specified sub-prompt.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the task type is not supported.
    /// </exception>
    public static Florence2Query CreateQuery(Florence2TaskType taskType, string subPrompt)
    {
        if (!TaskConfigurations.TryGetValue(taskType, out var config))
            throw new ArgumentException($"Unsupported task type: {taskType}");

        if (!config.RequiresSubPrompt)
            throw new ArgumentException($"Task {taskType} does not handle input parameter");

        return new Florence2Query(taskType, string.Format(config.Prompt, subPrompt));
    }
}

public record Florence2Query(Florence2TaskType TaskType, string Prompt);

public sealed class Florence2Result
{
    public Florence2TaskType TaskType { get; set; }

    // Text output (captions, OCR)
    public string? Text { get; set; }
    public float[] Vector { get; set; } = Array.Empty<float>();
    public float[] TextVector { get; set; } = Array.Empty<float>();

    // Detection/region outputs
    public List<SixLabors.ImageSharp.Rectangle>? BoundingBoxes { get; set; }
    public List<string>? Labels { get; set; }

    // Segmentation output
    public IReadOnlyCollection<IReadOnlyCollection<SixLabors.ImageSharp.Point>>? Polygons { get; set; }

    public override string ToString()
    {
        var value = TaskType switch {
            Florence2TaskType.Caption => Text ?? string.Empty,
            Florence2TaskType.DetailedCaption => Text ?? string.Empty,
            Florence2TaskType.MoreDetailedCaption => Text ?? string.Empty,
            Florence2TaskType.Ocr => Text ?? string.Empty,
            Florence2TaskType.OcrWithRegions => Text ?? string.Empty,
            Florence2TaskType.ObjectDetection => string.Join(", ", Labels ?? Enumerable.Empty<string>()),
            Florence2TaskType.DenseRegionCaption => string.Join(", ", Labels ?? Enumerable.Empty<string>()),
            Florence2TaskType.RegionProposal => ZipLabelsAndBoundingBoxes(Labels, BoundingBoxes),
            Florence2TaskType.CaptionToGrounding => string.Join(", ", Labels ?? Enumerable.Empty<string>()),
            Florence2TaskType.ReferringExpressionSegmentation => string.Join(", ",
                (Polygons ?? Enumerable.Empty<IReadOnlyCollection<SixLabors.ImageSharp.Point>>()).Select(p => string.Join(", ", p))),
            Florence2TaskType.RegionToSegmentation => string.Join(", ",
                (Polygons ?? Enumerable.Empty<IReadOnlyCollection<SixLabors.ImageSharp.Point>>()).Select(p => string.Join(", ", p))),
            Florence2TaskType.OpenVocabularyDetection => string.Join(", ", Labels ?? Enumerable.Empty<string>()),
            Florence2TaskType.RegionToCategory => string.Join(", ", Labels ?? Enumerable.Empty<string>()),
            Florence2TaskType.RegionToDescription => Text ?? string.Empty,
            _ => string.Empty
        };

        return $"{TaskType}: {value}";
    }

    private static string ZipLabelsAndBoundingBoxes(IEnumerable<string>? label, IEnumerable<SixLabors.ImageSharp.Rectangle>? boundingBox)
    {
        return string.Join(Environment.NewLine, (label ?? []).Zip((boundingBox ?? []), (l, b) => $"'{l}' -> [{b.Width}, {b.Height}]"));
    }
}

public class EncoderPreProcessor
{
    public (DenseTensor<float> Features, DenseTensor<long> AttentionMask) Process(
        Tensor<float> visionFeatures,
        Tensor<float> textFeatures,
        IReadOnlyCollection<string> tokenized)
    {
        var projectedFeatures = ConcatenateTensors(visionFeatures, textFeatures, 1);

        var visionAttentionMask = CreateAttentionMask(Enumerable.Range(0, visionFeatures.Dimensions[1]).ToArray(), _ => 1L);
        Debug.Assert(visionFeatures.Dimensions[1] == visionAttentionMask.Dimensions[1]);

        var textAttentionMask = CreateAttentionMask(tokenized, t => t == BartTokenizer.PadToken ? 0L : 1L);
        Debug.Assert(textFeatures.Dimensions[1] == textAttentionMask.Dimensions[1]);

        var projectedAttentionMask = ConcatenateTensors(visionAttentionMask, textAttentionMask, 1);

        return (projectedFeatures, projectedAttentionMask);
    }

    private static Tensor<TOut> CreateAttentionMask<TIn, TOut>(IReadOnlyCollection<TIn> data, Func<TIn, TOut> maskEvaluator)
    {
        var maskData = data.Select(maskEvaluator).ToArray();
        return new DenseTensor<TOut>(maskData, [1, data.Count]);
    }

    /// <summary>
    /// Concatenate two tensors along a specified axis
    /// </summary>
    /// <param name="tensor1">The first tensor to concatenate</param>
    /// <param name="tensor2">The second tensor to concatenate</param>
    /// <param name="axis">The axis along which to concatenate the tensors.</param>
    /// <typeparam name="T">The type of the tensor elements.</typeparam>
    /// <returns>
    /// The concatenated tensor.
    /// </returns>
    /// <exception cref="ArgumentException"></exception>
    private static DenseTensor<T> ConcatenateTensors<T>(Tensor<T> tensor1, Tensor<T> tensor2, int axis)
    {
        if (tensor1.Rank != tensor2.Rank)
            throw new ArgumentException("Tensors must have the same number of dimensions");

        if (axis < 0 || axis >= tensor1.Rank)
            throw new ArgumentException("Invalid axis");

        if (axis != 1)
            throw new ArgumentException("Only concatenation along axis 1 is supported");

        var newDimensions = tensor1.Dimensions.ToArray();
        newDimensions[axis] += tensor2.Dimensions[axis];

        var result = new DenseTensor<T>(newDimensions);

        // Copy data from tensor1
        for (int i = 0; i < tensor1.Length; i++) {
            result.SetValue(i, tensor1.GetValue(i));
        }

        // Copy data from tensor2
        var offset = (int)tensor1.Length;
        for (int i = 0; i < tensor2.Length; i++) {
            result.SetValue(offset + i, tensor2.GetValue(i));
        }

        return result;
    }
}

public partial class DecoderPostProcessor
{
    [GeneratedRegex(@"(\w+)(<loc_(\d+)><loc_(\d+)><loc_(\d+)><loc_(\d+)>)+", RegexOptions.Compiled)]
    private static partial Regex CategoryAndRegionRegex();

    [GeneratedRegex(
        @"([^<]+)(?:<loc_(\d+)><loc_(\d+)><loc_(\d+)><loc_(\d+)><loc_(\d+)><loc_(\d+)><loc_(\d+)><loc_(\d+)>)")]
    private static partial Regex CategoryAndQuadBoxRegex();

    [GeneratedRegex(@"<loc_(\d+)>")]
    private static partial Regex PointRegex();

    [GeneratedRegex(@"(\w+)<poly>(<loc_(\d+)>)+</poly>")]
    private static partial Regex LabeledPolygonsRegex();


    public Florence2Result Process(string modelOutput, Florence2TaskType taskType, bool imageWasPadded, int imageWidth, int imageHeight)
    {
        Florence2Tasks.TaskConfigurations.TryGetValue(taskType, out var taskConfig);

        // Florence2TaskType.OpenVocabularyDetection: arms<poly><loc_550><loc_421><loc_686><loc_510><loc_671><loc_740><loc_540><loc_616></poly>

        return taskConfig switch {
            // Advanced detection tasks, returns quad boxes.
            { ReturnsLabels: true, ReturnsBoundingBoxes: true, ReturnsPolygons: true } => ProcessPointsAsQuadBoxes(taskType, modelOutput, imageWasPadded, imageWidth, imageHeight),

            // Detection tasks
            { ReturnsLabels: true, ReturnsBoundingBoxes: true } => ProcessPointsAsBoundingBoxes(taskType, modelOutput, imageWasPadded, imageWidth, imageHeight),

            // Complex tasks
            { ReturnsLabels: true, ReturnsPolygons: true } => ProcessLabeledPolygons(taskType, modelOutput, imageWasPadded, imageWidth, imageHeight),

            // Complex tasks
            { ReturnsPolygons: true } => ProcessPointsAsPolygons(taskType, modelOutput, imageWasPadded, imageWidth, imageHeight),

            // Text generation tasks (captions, OCR)
            { ReturnsText: true } => new Florence2Result { TaskType = taskType, Text = modelOutput },

            // Region tasks - returns text probably
            // Florence2TaskType.RegionToDescription or
            //     Florence2TaskType.RegionToCategory or
            //     Florence2TaskType.RegionToOcr => await ProcessRegionResult(taskType, modelOutput),

            _ => throw new ArgumentException($"Unsupported task type: {taskType}")
        };
    }

    private Florence2Result ProcessPointsAsBoundingBoxes(Florence2TaskType taskType, string modelOutput, bool imageWasPadded, int imageWidth, int imageHeight)
    {
        // NOTE: "wheel" has two bounding boxes, "door" has one
        // example data: car<loc_54><loc_375><loc_906><loc_707>door<loc_710><loc_276><loc_908><loc_537>wheel<loc_708><loc_557><loc_865><loc_704><loc_147><loc_563><loc_305><loc_705>
        // regex that parses one or more "(category)(one or more groups of 4 loc-tokens)"
        var regex = CategoryAndRegionRegex();

        var w = imageWidth / 1000f;
        var h = imageHeight / 1000f;

        List<string> labels = new();
        List<SixLabors.ImageSharp.Rectangle> boundingBoxes = new();

        Match match = regex.Match(modelOutput);
        while (match.Success) {
            var label = match.Groups[1].Value;
            var captureCount = match.Groups[2].Captures.Count;
            for (int i = 0; i < captureCount; i++) {
                var x1 = int.Parse(match.Groups[3].Captures[i].Value);
                var y1 = int.Parse(match.Groups[4].Captures[i].Value);
                var x2 = int.Parse(match.Groups[5].Captures[i].Value);
                var y2 = int.Parse(match.Groups[6].Captures[i].Value);

                labels.Add(label);
                boundingBoxes.Add(new SixLabors.ImageSharp.Rectangle(
                    (int)((0.5f + x1) * w),
                    (int)((0.5f + y1) * h),
                    (int)((x2 - x1) * w),
                    (int)((y2 - y1) * h)));
            }

            match = match.NextMatch();
        }

        return new Florence2Result { TaskType = taskType, BoundingBoxes = boundingBoxes, Labels = labels };
    }

    private Florence2Result ProcessPointsAsQuadBoxes(Florence2TaskType taskType, string modelOutput, bool imageWasPadded, int imageWidth, int imageHeight)
    {
        // Regex to match text followed by 8 location coordinates
        var regex = CategoryAndQuadBoxRegex();

        var matches = regex.Matches(modelOutput);

        var quadBoxes = new List<IReadOnlyCollection<SixLabors.ImageSharp.Point>>();
        var labels = new List<string>();

        var w = imageWidth / 1000f;
        var h = imageHeight / 1000f;

        foreach (Match match in matches) {
            var text = match.Groups[1].Value;

            // Extract all 8 coordinates
            var points = new SixLabors.ImageSharp.Point[4];
            for (int i = 0; i < 8; i += 2) {
                // Add 2 to group index because group[1] is the text
                var valueX = 0.5f + int.Parse(match.Groups[i + 2].Value);
                var valueY = 0.5f + int.Parse(match.Groups[i + 3].Value);

                // Convert from 0-1000 range to image coordinates
                points[i / 2] = new SixLabors.ImageSharp.Point(
                    (int)(valueX * w),
                    (int)(valueY * h));
            }

            quadBoxes.Add(points);

            labels.Add(text);
        }

        // If you need to maintain compatibility with existing Rectangle format,
        // you could compute bounding rectangles that encompass each quad:
        var boundingBoxes = quadBoxes.Select(quad => {
            var minX = quad.Min(p => p.X);
            var minY = quad.Min(p => p.Y);
            var maxX = quad.Max(p => p.X);
            var maxY = quad.Max(p => p.Y);

            return new SixLabors.ImageSharp.Rectangle(minX, minY, maxX - minX, maxY - minY);
        }).ToList();

        return new Florence2Result {
            TaskType = taskType,
            BoundingBoxes = boundingBoxes,
            Labels = labels,
            Polygons = quadBoxes
        };
    }

    private Florence2Result ProcessLabeledPolygons(Florence2TaskType taskType, string modelOutput, bool imageWasPadded, int imageWidth, int imageHeight)
    {
        var regex = LabeledPolygonsRegex();
        var match = regex.Match(modelOutput);

        var labels = new List<string>();
        var polygons = new List<IReadOnlyCollection<SixLabors.ImageSharp.Point>>();
        var w = imageWidth / 1000f;
        var h = imageHeight / 1000f;

        while (match.Success) {
            var label = match.Groups[1].Value;
            var polygon = new List<SixLabors.ImageSharp.Point>();
            var coordinates = match.Groups[3];

            for (int i = 0; i < coordinates.Captures.Count; i += 2) {
                var x = (int)((0.5f + int.Parse(coordinates.Captures[i].Value)) * w);
                var y = (int)((0.5f + int.Parse(coordinates.Captures[i + 1].Value)) * h);
                polygon.Add(new SixLabors.ImageSharp.Point(x, y));
            }

            labels.Add(label);
            polygons.Add(polygon);

            match = match.NextMatch();
        }

        return new Florence2Result { TaskType = taskType, Labels = labels, Polygons = polygons };
    }

    private Florence2Result ProcessPointsAsPolygons(Florence2TaskType taskType, string modelOutput, bool imageWasPadded, int imageWidth, int imageHeight)
    {
        var regex = PointRegex();
        var matches = regex.EnumerateMatches(modelOutput);

        // for now, we only support a single polygon
        var polygons = new List<IReadOnlyCollection<SixLabors.ImageSharp.Point>>();
        var polygon = new List<SixLabors.ImageSharp.Point>();
        polygons.Add(polygon);

        var w = imageWidth / 1000f;
        var h = imageHeight / 1000f;

        // With match "<loc_XX>" the X is at index 5, and has the length match.Length - 5 - 1
        const int offset = 5;
        const int lengthOffset = 6;

        int count = 0;
        int x = 0;
        foreach (var match in matches) {
            var matchOffset = match.Index + offset;
            var matchLength = match.Length - lengthOffset;
            if (count % 2 == 0) {
                x = (int)((0.5f + int.Parse(modelOutput.AsSpan(matchOffset, matchLength))) * w);
            }
            else {
                var y = (int)((0.5f + int.Parse(modelOutput.AsSpan(matchOffset, matchLength))) * h);
                polygon.Add(new SixLabors.ImageSharp.Point(x, y));
            }

            count++;
        }

        return new Florence2Result { TaskType = taskType, Polygons = polygons };
    }
}

public static class AppFlorence
{
    private static readonly InferenceSession _visionEncoder;
    private static readonly InferenceSession _embedTokens;
    private static readonly InferenceSession _encoder;
    private static readonly InferenceSession _decoder;
    private static readonly BartTokenizer _tokenizer;
    private static readonly EncoderPreProcessor _encoderPreprocessor;
    private static readonly DecoderPostProcessor _postProcessor;

    private const int ImageSize = 768;
    private const float RescaleFactor = 1f/255f;
    private static readonly float[] ImageMean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] ImageStd = [0.229f, 0.224f, 0.225f];

    private const long BosTokenId = 0;
    private const long EosTokenId = 2;
    private const long PadTokenId = 1;

    static AppFlorence()
    {
        var sessionOptions = new SessionOptions {
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        _visionEncoder = new(AppConsts.FileVisionEncoder, sessionOptions);
        _embedTokens = new(AppConsts.FileEmbedTokens, sessionOptions);
        _encoder = new(AppConsts.FileEncoderModel, sessionOptions);
        _decoder = new(AppConsts.FileDecoderModel, sessionOptions);

        _tokenizer = BartTokenizer.FromPretrained();
        _encoderPreprocessor = new EncoderPreProcessor();
        _postProcessor = new DecoderPostProcessor();
    } 

    public static DenseTensor<float> ProcessImage(Image<Rgb24> image, bool padToSquare = true)
    {
        // Clone the image to avoid modifying the original
        using var processedImage = image.CloneAs<Rgb24>();
        image.Mutate(ctx => {
            ctx.Resize(new ResizeOptions {
                Size = new SixLabors.ImageSharp.Size(ImageSize, ImageSize),
                Mode = padToSquare ? ResizeMode.Pad : ResizeMode.Stretch, // Pad to maintain aspect ratio
                PadColor = SixLabors.ImageSharp.Color.Black // Use black for padding
            });
        });
        var tensor = new DenseTensor<float>([1, 3, ImageSize, ImageSize]);
        image.ProcessPixelRows(accessor => {
            for (var y = 0; y < accessor.Height; y++) {
                var pixelRow = accessor.GetRowSpan(y);
                for (var x = 0; x < pixelRow.Length; x++) {
                    // Get RGB values
                    var pixel = pixelRow[x];

                    // Convert to float and normalize [0,1]
                    // Apply mean/std normalization
                    // Store in CHW format
                    tensor[0, 0, y, x] = (pixel.R * RescaleFactor - ImageMean[0]) / ImageStd[0]; // Red channel
                    tensor[0, 1, y, x] = (pixel.G * RescaleFactor - ImageMean[1]) / ImageStd[1]; // Green channel
                    tensor[0, 2, y, x] = (pixel.B * RescaleFactor - ImageMean[2]) / ImageStd[2]; // Blue channel
                }
            }
        });
        return tensor;
    }

    private static IDisposableReadOnlyCollection<DisposableNamedOnnxValue> RunInference(
        InferenceSession session,
        IReadOnlyCollection<NamedOnnxValue> inputs)
    {
        return session.Run(inputs);
    }

    private static long GetNextToken(Tensor<float> logits)
    {
        // Get last position logits
        var lastLogits = logits.Dimensions[1] - 1;
        var vocabSize = logits.Dimensions[2];

        // Find max probability token
        var maxProb = float.MinValue;
        var maxToken = 0L;

        for (int i = 0; i < vocabSize; i++) {
            var prob = logits[0, lastLogits, i];
            if (prob > maxProb) {
                maxProb = prob;
                maxToken = i;
            }
        }

        return maxToken;
    }

    public static Tensor<float> RunVisionEncoder(DenseTensor<float> imageInput)
    {
        // Run vision encoder to get image features
        // Input: pixel_values [batch_size, 3, height, width]
        // Output: image_features [batch_size, sequence_length, 768]
        var visionInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("pixel_values", imageInput)
        };

        using var visionOutput = RunInference(_visionEncoder, visionInputs);
        var imageFeaturesTensor = visionOutput.First(o => o.Name == "image_features");
        return imageFeaturesTensor.Value as Tensor<float> ?? throw new InvalidCastException("image_features tensor is not of type Tensor<float>");
    }

    public static Tensor<float> EmbedTokens(Tensor<long> tokens)
    {
        // Run token embedding model to get text features
        // Input: input_ids [batch_size, sequence_length]
        // Output: inputs_embeds [batch_size, sequence_length, 768]
        var embedInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", tokens)
        };

        using var embedOutput = RunInference(_embedTokens, embedInputs);
        var textFeaturesTensor = embedOutput.First(o => o.Name == "inputs_embeds").AsTensor<float>();
        return textFeaturesTensor;
    }

    public static Tensor<float> RunEncoder(Tensor<float> embeddings, Tensor<long> attentionMask)
    {
        // Step 2: Run encoder on image features
        // Inputs: 
        // - inputs_embeds [batch_size, encoder_sequence_length, 768]
        // - attention_mask [batch_size, encoder_sequence_length]
        var encoderInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("inputs_embeds", embeddings),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
        };

        using var encoderOutput = RunInference(_encoder, encoderInputs);
        var encoderHiddenStates = encoderOutput.First(o => o.Name == "last_hidden_state").AsTensor<float>();
        return encoderHiddenStates;
    }

    public static IReadOnlyCollection<long> RunDecoder(Tensor<float> encoderHiddenStates, Tensor<long> encoderAttentionMask, int maxLength = 1024)
    {
        // this value comes from the "config.json" of the "onnx-community/Florence-2-*" repo.
        const int decoderStartTokenId = 2; // Initialize with decoder start token (end token?)
        const int eosTokenId = 2; // End of sentence token, TODO: we could get this from the tokenizer

        // Initialize with decoder start token
        var generatedTokens = new List<long> { decoderStartTokenId };

        // dry run???
        {
            // Create decoder inputs from current tokens
            var decoderInputIds = new DenseTensor<long>(
                generatedTokens.ToArray(),
                [1, generatedTokens.Count]
            );

            var decoderEmbeddings = EmbedTokens(decoderInputIds);

            // Run decoder
            NamedOnnxValue[] decoderInputs =
            [
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenStates),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", encoderAttentionMask),
                NamedOnnxValue.CreateFromTensor("inputs_embeds", decoderEmbeddings)
            ];

            _ = RunInference(_decoder, decoderInputs);
            // var logits = outputs.First(o => o.Name == "logits").AsTensor<float>();
        }

        for (var i = 0; i < maxLength; i++) {
            // Create decoder inputs from current tokens
            var decoderInputIds = new DenseTensor<long>(
                generatedTokens.ToArray(),
                [1, generatedTokens.Count]
            );

            var decoderEmbeddings = EmbedTokens(decoderInputIds);

            // Run decoder
            NamedOnnxValue[] decoderInputs =
            [
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenStates),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", encoderAttentionMask),
                NamedOnnxValue.CreateFromTensor("inputs_embeds", decoderEmbeddings)
            ];

            var outputs = RunInference(_decoder, decoderInputs);
            var logits = outputs.First(o => o.Name == "logits").AsTensor<float>();

            // Get next token (greedy selection from last position)
            var nextToken = GetNextToken(logits);

            // Stop if we hit EOS token
            if (nextToken == eosTokenId)
                break;

            generatedTokens.Add(nextToken);
        }

        return generatedTokens;
    }

    public static Florence2Result Process(Image<Rgb24> image, Florence2Query query)
    {
        var (taskType, prompt) = query;

        if (string.IsNullOrWhiteSpace(prompt)) {
            throw new ArgumentException("Prompt cannot be empty");
        }

        // 1. Vision
        var processedImage = ProcessImage(image, false);
        var visionFeatures = RunVisionEncoder(processedImage);

        var batchSize = (int)visionFeatures.Dimensions[0];  // 1
        var seqLength = (int)visionFeatures.Dimensions[1];  // 577
        var hiddenSize = (int)visionFeatures.Dimensions[2]; // 768
        var vector = new float[hiddenSize];
        for (int h = 0; h < hiddenSize; h++) {
            vector[h] = visionFeatures[0, 0, h];  // [batch=0, sequence=0, hidden=h]
        }

        var norm = (float)Math.Sqrt(vector.Sum(t => t * t));
        Parallel.For(0, vector.Length, i => {
            vector[i] /= norm;
        });

        // 2. Text
        var tokenized = _tokenizer.Tokenize(prompt);
        Debug.WriteLine($"Input tokens: '{string.Join("', '", tokenized)}'");

        var inputIds = new DenseTensor<long>(_tokenizer.ConvertTokensToIds(tokenized).Select(i => (long)i).ToArray(), [1, tokenized.Count]);
        var textFeatures = EmbedTokens(inputIds);

        // 3. Concatenate vision and text features
        var (projectedFeatures, projectedAttentionMask) = _encoderPreprocessor.Process(visionFeatures, textFeatures, tokenized);

        // 4. Run encoder to get hidden states for decoder
        var encoderHiddenStates = RunEncoder(projectedFeatures, projectedAttentionMask);

        // 5. Decoder in autoregressive mode to generate output text
        var decoderOutput = RunDecoder(encoderHiddenStates, projectedAttentionMask);

        var text = _tokenizer.Decode(decoderOutput.Select(f => (int)f).ToList());

        // 6. Post-processing
        var result = _postProcessor.Process(text, taskType, true, image.Width, image.Height);
        result.Vector = vector;
        result.Text = string.Join(',', result.Labels!);
        return result;
    }

    public static List<string> ExtractKeywordsUsingFlorence(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new List<string>();

        var tokens = _tokenizer.Tokenize(text);

        return tokens
            .Where(t => t.Length > 3)
            .Where(t => !t.StartsWith("<") && !t.EndsWith(">"))
            .Distinct()
            .OrderByDescending(t => t.Length)
            .ToList();
    }

    public static float[] GetTextEmbedding(string text)
    {
        try {
            // 1. Токенизация через BartTokenizer (уже используется в Florence-2)
            var tokenized = _tokenizer.Tokenize(text);
            var inputIds = new DenseTensor<long>(_tokenizer.ConvertTokensToIds(tokenized).Select(i => (long)i).ToArray(), [1, tokenized.Count]);

            // 2. Получаем text embeddings через embed_tokens модель Florence-2
            var textFeatures = EmbedTokens(inputIds);

            // 3. Global Average Pooling по sequence dimension
            var seqLength = (int)textFeatures.Dimensions[1];
            var hiddenSize = (int)textFeatures.Dimensions[2];
            var compactVector = new float[hiddenSize];

            for (int h = 0; h < hiddenSize; h++) {
                float sum = 0f;
                for (int s = 0; s < seqLength; s++) {
                    sum += textFeatures[0, s, h];
                }
                compactVector[h] = sum / seqLength;
            }

            // 4. L2 нормализация
            var norm = (float)Math.Sqrt(compactVector.Sum(f => f * f));
            if (norm > 0) {
                for (int i = 0; i < compactVector.Length; i++) {
                    compactVector[i] /= norm;
                }
            }

            return compactVector;
        }
        catch (Exception ex) {
            Console.WriteLine($"Error getting Florence-2 text embedding: {ex.Message}");
            return new float[768]; // Empty 768-dimensional vector
        }
    }

    public static Florence2Result AnalyzeImage(Image<Rgb24> image)
    {
        var query = new Florence2Query(Florence2TaskType.ObjectDetection, "Locate the objects with category name in the image.");
        return Process(image, query);
    }

    public static float GetDistance(float[] x, float[] y)
    {
        if (x.Length == 0 || y.Length == 0 || x.Length != y.Length) {
            return 1.1f;
        }

        var distance = x.Select((t, i) => t * y[i]).Sum();
        distance = 1f - distance;
        return distance;
    }
}