﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace CASCExplorer
{
    public class CDNIndexHandler
    {
        private static readonly ByteArrayComparer comparer = new ByteArrayComparer();
        private readonly Dictionary<byte[], IndexEntry> CDNIndexData = new Dictionary<byte[], IndexEntry>(comparer);

        private CASCConfig CASCConfig;
        private AsyncAction worker;
        private SyncDownloader downloader;
        public static CDNCache Cache = new CDNCache("cache");

        public int Count
        {
            get { return CDNIndexData.Count; }
        }

        private CDNIndexHandler(CASCConfig cascConfig, AsyncAction worker)
        {
            CASCConfig = cascConfig;
            this.worker = worker;
            downloader = new SyncDownloader(worker);
        }

        public static CDNIndexHandler Initialize(CASCConfig config, AsyncAction worker)
        {
            var handler = new CDNIndexHandler(config, worker);

            if (worker != null)
            {
                worker.ThrowOnCancel();
                worker.ReportProgress(0, "Loading \"CDN indexes\"...");
            }

            for (int i = 0; i < config.Archives.Count; i++)
            {
                string archive = config.Archives[i];

                if (config.OnlineMode)
                    handler.DownloadIndexFile(archive, i);
                else
                    handler.OpenIndexFile(archive, i);

                if (worker != null)
                {
                    worker.ThrowOnCancel();
                    worker.ReportProgress((int)((float)i / (float)(config.Archives.Count - 1) * 100.0f));
                }
            }

            return handler;
        }

        private void ParseIndex(Stream stream, int i)
        {
            using (var br = new BinaryReader(stream))
            {
                stream.Seek(-12, SeekOrigin.End);
                int count = br.ReadInt32();
                stream.Seek(0, SeekOrigin.Begin);

                if (count * (16 + 4 + 4) > stream.Length)
                    throw new Exception("ParseIndex failed");

                for (int j = 0; j < count; ++j)
                {
                    byte[] key = br.ReadBytes(16);

                    if (key.IsZeroed()) // wtf?
                        key = br.ReadBytes(16);

                    if (key.IsZeroed()) // wtf?
                        throw new Exception("key.IsZeroed()");

                    IndexEntry entry = new IndexEntry();
                    entry.Index = i;
                    entry.Size = br.ReadInt32BE();
                    entry.Offset = br.ReadInt32BE();

                    CDNIndexData.Add(key, entry);
                }
            }
        }

        private void DownloadIndexFile(string archive, int i)
        {
            try
            {
                string file = CASCConfig.CDNPath + "/data/" + archive.Substring(0, 2) + "/" + archive.Substring(2, 2) + "/" + archive + ".index";
                string url = "http://" + CASCConfig.CDNHost + "/" + file;

                Stream stream = Cache.OpenFile(file, url, false);

                if (stream != null)
                {
                    ParseIndex(stream, i);
                }
                else
                {
                    using (var fs = downloader.OpenFile(url))
                        ParseIndex(fs, i);
                }
            }
            catch
            {
                throw new Exception("DownloadFile failed!");
            }
        }

        private void OpenIndexFile(string archive, int i)
        {
            try
            {
                string dataFolder = CASCGame.GetDataFolder(CASCConfig.GameType);
                string indexPath = String.Format("{0}\\indices\\", dataFolder);

                string path = Path.Combine(CASCConfig.BasePath, indexPath, archive + ".index");

                using (FileStream fs = new FileStream(path, FileMode.Open))
                    ParseIndex(fs, i);
            }
            catch
            {
                throw new Exception("OpenFile failed!");
            }
        }

        public Stream OpenDataFile(IndexEntry entry)
        {
            var archive = CASCConfig.Archives[entry.Index];

            string file = CASCConfig.CDNPath + "/data/" + archive.Substring(0, 2) + "/" + archive.Substring(2, 2) + "/" + archive;
            string url = "http://" + CASCConfig.CDNHost + "/" + file;

            Stream stream = Cache.OpenFile(file, url, true);

            if (stream != null)
            {
                stream.Position = entry.Offset;
                return stream;
            }

            HttpWebRequest req = WebRequest.CreateHttp(url);
            req.AddRange(entry.Offset, entry.Offset + entry.Size - 1);
            HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
            return resp.GetResponseStream();
        }

        public Stream OpenDataFileDirect(byte[] key)
        {
            if (worker != null)
            {
                worker.ThrowOnCancel();
                worker.ReportProgress(0, "Downloading file...");
            }

            var keyStr = key.ToHexString().ToLower();

            string file = CASCConfig.CDNPath + "/data/" + keyStr.Substring(0, 2) + "/" + keyStr.Substring(2, 2) + "/" + keyStr;
            string url = "http://" + CASCConfig.CDNHost + "/" + file;

            Stream stream = Cache.OpenFile(file, url, false);

            if (stream != null)
                return stream;

            return downloader.OpenFile(url);
        }

        public static Stream OpenConfigFileDirect(CASCConfig cfg, string key)
        {
            string file = cfg.CDNPath + "/config/" + key.Substring(0, 2) + "/" + key.Substring(2, 2) + "/" + key;
            string url = "http://" + cfg.CDNHost + "/" + file;

            Stream stream = Cache.OpenFile(file, url, false);

            if (stream != null)
                return stream;

            return OpenFileDirect(url);
        }

        public static Stream OpenFileDirect(string url)
        {
            HttpWebRequest req = WebRequest.CreateHttp(url);
            HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
            return resp.GetResponseStream();
        }

        public IndexEntry GetIndexInfo(byte[] key)
        {
            IndexEntry result;

            if (!CDNIndexData.TryGetValue(key, out result))
                Logger.WriteLine("CDNIndexHandler: missing index: {0}", key.ToHexString());

            return result;
        }

        public void Clear()
        {
            CDNIndexData.Clear();
        }
    }
}
