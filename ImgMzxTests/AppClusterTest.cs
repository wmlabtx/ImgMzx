using ImgMzx;
using System.Diagnostics;

namespace ImgMzxTests;

[TestClass]
public class AppClusterTest
{
    /*
    private static bool _databaseLoaded = false;

    [ClassInitialize]
    public static void ClassSetup(TestContext _)
    {
        Debug.WriteLine($"Test started at: {DateTime.Now}");
        if (!AppImgs.IsLoaded()) {
            try {
                var progress = new Progress<string>(p => Debug.WriteLine($"Loading database: {p}"));
                AppImgs.Load(AppConsts.FileDatabase, progress, out int maxImages);

                _databaseLoaded = true;
                Debug.WriteLine($"Database loaded successfully with {AppImgs.Count()} images");
            }
            catch (Exception ex) {
                Debug.WriteLine($"Failed to load database: {ex.Message}");
                _databaseLoaded = false;
            }
        }
        else {
            _databaseLoaded = true;
            Debug.WriteLine($"Database already loaded with {AppImgs.Count()} images");
        }
    }

    [TestMethod]
    public void TestInitClusters()
    {
        if (!_databaseLoaded || AppImgs.Count() < 2) {
            Assert.Inconclusive("Need at least 2 images in database for this test");
            return;
        }

        var progress = new Progress<string>(p => Debug.WriteLine(""));
        AppImgs.InitClusters(progress);
    }
    */

    /*
    [TestMethod]
    public void TrainClusters()
    {
        if (!_databaseLoaded || AppImgs.Count() < 2) {
            Assert.Inconclusive("Need at least 2 images in database for this test");
            return;
        }

        var allImages = AppImgs.GetRandomImages(Math.Min(20, AppImgs.Count()));
        Debug.WriteLine($"Selected {allImages.Count} random images from database for training");

        var counter = 1;
        foreach (var img in allImages) {
            if (img.Id > 0) {
                continue; // Skip already clustered images
            }

            var beam = AppImgs.GetBeam(img);
            var cpop = AppImgs.CheckForEmptyClusters();

            var oP = AppImgs.GetPopulation(img.Id);
            var oId = img.Id;
            var nId = AppImgs.CheckCluster(img, beam);
            img.SetId(nId);
            var nP = AppImgs.GetPopulation(nId);
            Debug.WriteLine($"{counter}: {img.Name} {oId} [{oP}] {AppConsts.CharRightArrow} {nId} [{nP}]");
            

            if (counter % 10 == 0) {
                Debug.WriteLine($"Clusters: #{cpop.First().Item1} ({cpop.First().Item2}) to #{cpop.Last().Item1} ({cpop.Last().Item2})");
            }

            counter++;
        }
    }
    */
}