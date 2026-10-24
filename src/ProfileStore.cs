using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class GameProfileEntry
    {
        public string Name;
        public string Path;
        public int Order;
        public DateTime Created;
    }

    public static class ProfileStore
    {
        public static string GamesDir
        {
            get { return ConfigStore.GamesDir; }
        }

        public static List<GameProfileEntry> List()
        {
            List<GameProfileEntry> list = new List<GameProfileEntry>();
            try
            {
                if (!Directory.Exists(GamesDir)) Directory.CreateDirectory(GamesDir);
                string[] files = Directory.GetFiles(GamesDir, "*.xml");
                for (int i = 0; i < files.Length; i++)
                {
                    AppConfig cfg = ConfigStore.LoadFrom(files[i]);
                    if (cfg == null) continue;
                    GameProfileEntry entry = new GameProfileEntry();
                    entry.Path = files[i];
                    entry.Name = string.IsNullOrEmpty(cfg.GameName)
                        ? Path.GetFileNameWithoutExtension(files[i])
                        : cfg.GameName;
                    entry.Order = cfg.ProfileOrder;
                    try { entry.Created = File.GetCreationTime(files[i]); } catch { entry.Created = DateTime.MinValue; }
                    list.Add(entry);
                }
            }
            catch { }
            list.Sort(delegate(GameProfileEntry a, GameProfileEntry b)
            {
                int order = a.Order.CompareTo(b.Order);
                if (order != 0) return order;
                int created = a.Created.CompareTo(b.Created);
                if (created != 0) return created;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        public static GameProfileEntry Save(string name, AppConfig config)
        {
            if (config == null) return null;
            string clean = string.IsNullOrEmpty(name) ? "新游戏" : name.Trim();
            if (clean.Length == 0) clean = "新游戏";
            config.GameName = clean;
            string file = SafeFileName(clean) + ".xml";
            string path = Path.Combine(GamesDir, file);
            if (!ConfigStore.SaveTo(path, config)) return null;
            GameProfileEntry entry = new GameProfileEntry();
            entry.Name = clean;
            entry.Path = path;
            return entry;
        }

        public static GameProfileEntry CreateNew(string name)
        {
            AppConfig config = AppConfig.CreateDefault();
            config.GameName = string.IsNullOrEmpty(name) ? "新游戏" : name.Trim();
            config.GamePath = "";
            config.ProfileOrder = NextOrder();
            return Save(config.GameName, config);
        }

        public static int NextOrder()
        {
            int max = 0;
            try
            {
                List<GameProfileEntry> list = List();
                for (int i = 0; i < list.Count; i++) if (list[i].Order > max) max = list[i].Order;
            }
            catch { }
            return max + 1;
        }

        public static string AssetDir(string profilePath)
        {
            if (string.IsNullOrEmpty(profilePath)) return "";
            string stem = Path.GetFileNameWithoutExtension(profilePath);
            return Path.Combine(GamesDir, "assets", stem);
        }

        public static void SaveAssets(string profilePath, AppConfig config)
        {
            if (string.IsNullOrEmpty(profilePath) || config == null) return;
            try
            {
                string dir = AssetDir(profilePath);
                Directory.CreateDirectory(dir);
                if (config.ImageProjects != null)
                {
                    for (int project = 0; project < config.ImageProjects.Count; project++)
                    {
                        AutoImageProject imageProject = config.ImageProjects[project];
                        if (imageProject == null || imageProject.Features == null) continue;
                        for (int slot = 0; slot < imageProject.Features.Count && slot < 3; slot++)
                        {
                            AutoFeatureConfig feature = imageProject.Features[slot];
                            if (feature == null || string.IsNullOrEmpty(feature.ImageFile)) continue;
                            CopyIfDifferent(ConfigStore.FeaturePath(project, slot),
                                Path.Combine(dir, AutoImageName(project, slot)));
                        }
                    }
                }
                if (config.StorySkip != null && config.StorySkip.Features != null)
                {
                    for (int target = 0; target < config.StorySkip.Features.Count && target < 3; target++)
                    {
                        StorySkipFeatureConfig feature = config.StorySkip.Features[target];
                        if (feature == null || feature.Images == null) continue;
                        for (int image = 0; image < feature.Images.Count && image < 3; image++)
                        {
                            StorySkipImageConfig item = feature.Images[image];
                            if (item == null || string.IsNullOrEmpty(item.ImageFile)) continue;
                            CopyIfDifferent(ConfigStore.StorySkipPath(target, image),
                                Path.Combine(dir, "skip" + (target + 1) + "_" + (image + 1) + ".png"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("保存游戏图片资源失败: " + ex.Message);
            }
        }

        public static void CopyAssets(string oldProfilePath, string newProfilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(oldProfilePath) || string.IsNullOrEmpty(newProfilePath)) return;
                string oldDir = AssetDir(oldProfilePath);
                if (!Directory.Exists(oldDir)) return;
                string newDir = AssetDir(newProfilePath);
                Directory.CreateDirectory(newDir);
                string[] files = Directory.GetFiles(oldDir);
                for (int i = 0; i < files.Length; i++)
                    File.Copy(files[i], Path.Combine(newDir, Path.GetFileName(files[i])), true);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("复制游戏图片资源失败: " + ex.Message);
            }
        }

        public static void LoadAssets(string profilePath)
        {            if (string.IsNullOrEmpty(profilePath)) return;
            try
            {
                string dir = AssetDir(profilePath);
                if (!Directory.Exists(dir)) return;
                for (int mode = 0; mode < 12; mode++)
                {
                    for (int slot = 0; slot < 3; slot++)
                    {
                        string source = Path.Combine(dir, AutoImageName(mode, slot));
                        // 旧存档只保存了全局 featureN.png，首次载入时迁移到模式一。
                        if (!File.Exists(source) && mode == 0)
                            source = Path.Combine(dir, "feature" + (slot + 1) + ".png");
                        CopyIfDifferent(source, ConfigStore.FeaturePath(mode, slot));
                    }
                }
                for (int target = 0; target < 3; target++)
                    for (int image = 0; image < 3; image++)
                        CopyIfDifferent(Path.Combine(dir, "skip" + (target + 1) + "_" + (image + 1) + ".png"),
                            ConfigStore.StorySkipPath(target, image));
            }
            catch (Exception ex)
            {
                ConfigStore.Log("加载游戏图片资源失败: " + ex.Message);
            }
        }

        private static string AutoImageName(int mode, int slot)
        {
            return "feature" + (mode + 1) + "_" + (slot + 1) + ".png";
        }

        private static void CopyIfDifferent(string source, string destination)
        {
            if (!File.Exists(source)) return;
            try
            {
                FileInfo src = new FileInfo(source);
                if (File.Exists(destination))
                {
                    FileInfo dst = new FileInfo(destination);
                    if (src.Length == dst.Length && src.LastWriteTimeUtc == dst.LastWriteTimeUtc) return;
                }
                string dir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.Copy(source, destination, true);
            }
            catch { }
        }

        public static string SafeFileName(string name)
        {
            string result = name == null ? "" : name.Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++) result = result.Replace(invalid[i], '_');
            result = result.Replace(' ', '_');
            if (result.Length == 0) result = "game";
            if (result.Length > 48) result = result.Substring(0, 48);
            return result;
        }

        // assetsDir 为该游戏配置的专属资源文件夹；传入后导出内容与当前活动游戏无关。
        public static bool Export(AppConfig config, string zipPath, string assetsDir)
        {
            string temp = Path.Combine(Path.GetTempPath(), "mobui-export-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (string.IsNullOrEmpty(assetsDir)) assetsDir = ConfigStore.AssetsDir;
                Directory.CreateDirectory(temp);
                if (!ConfigStore.SaveTo(Path.Combine(temp, "config.xml"), config)) return false;
                string images = Path.Combine(temp, "key_images");
                string imageDir = Path.Combine(assetsDir, "key_images");
                if (Directory.Exists(imageDir))
                {
                    Directory.CreateDirectory(images);
                    string[] files = Directory.GetFiles(imageDir);
                    for (int i = 0; i < files.Length; i++)
                    {
                        File.Copy(files[i], Path.Combine(images, Path.GetFileName(files[i])), true);
                    }
                }
                int projectCount = config.ImageProjects == null ? 0 : config.ImageProjects.Count;
                for (int mode = 0; mode < projectCount; mode++)
                {
                    for (int slot = 0; slot < 3; slot++)
                    {
                        string feature = Path.Combine(assetsDir, AutoImageName(mode, slot));
                        if (File.Exists(feature))
                            File.Copy(feature, Path.Combine(temp, AutoImageName(mode, slot)), true);
                    }
                }
                for (int target = 0; target < 3; target++)
                {
                    for (int image = 0; image < 3; image++)
                    {
                        string skip = Path.Combine(assetsDir, "skip" + (target + 1) + "_" + (image + 1) + ".png");
                        if (File.Exists(skip))
                            File.Copy(skip, Path.Combine(temp, "skip" + (target + 1) + "_" + (image + 1) + ".png"), true);
                    }
                }
                File.WriteAllText(Path.Combine(temp, "分享说明.txt"),
                    "移动端UI配置分享包\r\n导入后会自动载入全部模式、按键布局、摇杆、视角操控、自动切换模式和图片。\r\n");
                if (File.Exists(zipPath)) File.Delete(zipPath);
                // 手动建包：条目名统一用 “/”，避免解压工具把 key_images\xx.png 当成文件名。
                using (FileStream fs = new FileStream(zipPath, FileMode.CreateNew))
                using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    AddZipDirectory(archive, temp, temp);
                }
                return true;
            }
            catch (Exception ex)
            {
                ConfigStore.Log("导出配置失败: " + ex.Message);
                return false;
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        }

        private static void AddZipDirectory(ZipArchive archive, string root, string dir)
        {
            string[] files = Directory.GetFiles(dir);
            for (int i = 0; i < files.Length; i++)
            {
                string entryName = files[i].Substring(root.Length).TrimStart('\\', '/');
                entryName = entryName.Replace('\\', '/');
                archive.CreateEntryFromFile(files[i], entryName, CompressionLevel.Optimal);
            }
            string[] dirs = Directory.GetDirectories(dir);
            for (int i = 0; i < dirs.Length; i++) AddZipDirectory(archive, root, dirs[i]);
        }

        public static AppConfig Import(string zipPath, out string error)
        {
            error = "";
            string temp = Path.Combine(Path.GetTempPath(), "mobui-import-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                ZipFile.ExtractToDirectory(zipPath, temp);
                string configPath = Path.Combine(temp, "config.xml");
                if (!File.Exists(configPath))
                {
                    error = "分享包里找不到 config.xml";
                    return null;
                }
                AppConfig config = ConfigStore.LoadFrom(configPath);
                if (config == null)
                {
                    error = "配置文件损坏";
                    return null;
                }
                // 导入的资源直接落到该游戏配置的同名专属文件夹，
                // 而不是当前正在使用的游戏目录，避免游戏之间资源串扰。
                if (string.IsNullOrEmpty(config.GameName))
                    config.GameName = Path.GetFileNameWithoutExtension(zipPath);
                string targetDir = ConfigStore.GameAssetsDir(config.GameName);
                string imageDir = Path.Combine(targetDir, "key_images");
                if (!Directory.Exists(imageDir)) Directory.CreateDirectory(imageDir);
                string images = Path.Combine(temp, "key_images");
                if (Directory.Exists(images))
                {
                    string[] files = Directory.GetFiles(images);
                    for (int i = 0; i < files.Length; i++)
                    {
                        File.Copy(files[i], Path.Combine(imageDir, Path.GetFileName(files[i])), true);
                    }
                }
                int projectCount = config.ImageProjects == null ? 0 : config.ImageProjects.Count;
                for (int mode = 0; mode < projectCount; mode++)
                {
                    for (int slot = 0; slot < 3; slot++)
                    {
                        string feature = Path.Combine(temp, AutoImageName(mode, slot));
                        // 旧分享包只有全局 featureN.png，导入时放入项目一。
                        if (!File.Exists(feature) && mode == 0)
                            feature = Path.Combine(temp, "feature" + (slot + 1) + ".png");
                        if (File.Exists(feature))
                            File.Copy(feature, Path.Combine(targetDir, AutoImageName(mode, slot)), true);
                    }
                }
                for (int target = 0; target < 3; target++)
                {
                    for (int image = 0; image < 3; image++)
                    {
                        string skip = Path.Combine(temp, "skip" + (target + 1) + "_" + (image + 1) + ".png");
                        if (File.Exists(skip))
                            File.Copy(skip, Path.Combine(targetDir,
                                "skip" + (target + 1) + "_" + (image + 1) + ".png"), true);
                    }
                }
                return config;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        }
    }
}
