﻿using BeatSaberPlaylistsLib.Types;
using MorePlaylists.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/*
 * Yoinked from Playlists Lib with some changes
 * Original Author: Zingabopp
 */

namespace MorePlaylists.Entries
{
    public abstract class GenericEntry : IGenericEntry, IDeferredSpriteLoad
    {
        private BeatSaberPlaylistsLib.Types.IPlaylist _playlist = null;
        private DownloadState _downloadState = DownloadState.None;
        protected Sprite _sprite;

        protected static readonly Queue<Action> SpriteQueue = new Queue<Action>();
        protected bool SpriteLoadQueued;
        private static readonly object _loaderLock = new object();
        private static bool CoroutineRunning = false;

        public Sprite Sprite
        {
            get
            {
                if (_sprite == null)
                {
                    if (!SpriteLoadQueued)
                    {
                        SpriteLoadQueued = true;
                        QueueLoadSprite(this);
                    }

                    return BeatSaberMarkupLanguage.Utilities.ImageResources.BlankSprite;
                }

                return _sprite;
            }
        }

        public bool SpriteWasLoaded { get; private set; }
        public event EventHandler SpriteLoaded;
        public event Action FinishedDownload;

        public abstract string Title { get; protected set; }
        public abstract string Author { get; protected set; }
        public abstract string Description { get; protected set; }
        public abstract string PlaylistURL { get; protected set; }

        public BeatSaberPlaylistsLib.Types.IPlaylist RemotePlaylist
        {
            get
            {
                if (_playlist == null && DownloadState != DownloadState.Downloading)
                {
                    DownloadState = DownloadState.Downloading;
                    DownloadPlaylist();
                }

                return _playlist;
            }
            private set
            {
                _playlist = value;
                DownloadState = value == null ? DownloadState.None : DownloadState.DownloadedPlaylist;
            }
        }

        public BeatSaberPlaylistsLib.Types.IPlaylist LocalPlaylist { get; set; }

        public DownloadState DownloadState
        {
            get => _downloadState;
            set
            {
                _downloadState = value;
                if (value == DownloadState.DownloadedPlaylist || value == DownloadState.Error)
                {
                    FinishedDownload?.Invoke();
                }
            }
        }

        public bool DownloadBlocked { get; set; }

        private async void DownloadPlaylist()
        {
            try
            {
                Stream playlistStream = new MemoryStream(await DownloaderUtils.instance.DownloadFileToBytesAsync(PlaylistURL));
                RemotePlaylist = BeatSaberPlaylistsLib.PlaylistManager.DefaultManager.DefaultHandler?.Deserialize(playlistStream);
            }
            catch (Exception e)
            {
                if (!(e is TaskCanceledException || e.Message.ToUpper().Contains("ABORTED")))
                {
                    Plugin.Log.Critical("An exception occurred while acquiring " + PlaylistURL + "\nException: " + e.Message);
                    DownloadState = DownloadState.Error;
                }
                else
                {
                    DownloadState = DownloadState.None;
                }
            }
        }

        public abstract Stream GetCoverStream();

        private static void QueueLoadSprite(GenericEntry spriteEntry)
        {
            SpriteQueue.Enqueue(() =>
            {
                try
                {
                    using (Stream imageStream = spriteEntry.GetCoverStream())
                    {
                        byte[] imageBytes = new byte[imageStream.Length];
                        imageStream.Read(imageBytes, 0, (int)imageStream.Length);
                        spriteEntry._sprite = BeatSaberMarkupLanguage.Utilities.LoadSpriteRaw(imageBytes);
                        if (spriteEntry._sprite != null)
                        {
                            spriteEntry.SpriteWasLoaded = true;
                        }
                        else
                        {
                            spriteEntry._sprite = BeatSaberMarkupLanguage.Utilities.ImageResources.BlankSprite;
                            spriteEntry.SpriteWasLoaded = true;
                        }
                    }

                    spriteEntry.SpriteLoaded?.Invoke(spriteEntry, null);
                    spriteEntry.SpriteLoadQueued = false;
                }
                catch (Exception)
                {
                    spriteEntry._sprite = BeatSaberMarkupLanguage.Utilities.ImageResources.BlankSprite;
                    spriteEntry.SpriteWasLoaded = true;
                    spriteEntry.SpriteLoaded?.Invoke(spriteEntry, null);
                }
            });

            if (!CoroutineRunning)
                SharedCoroutineStarter.instance.StartCoroutine(SpriteLoadCoroutine());
        }

        public static YieldInstruction LoadWait = new WaitForEndOfFrame();

        private static IEnumerator<YieldInstruction> SpriteLoadCoroutine()
        {
            lock (_loaderLock)
            {
                if (CoroutineRunning)
                    yield break;
                CoroutineRunning = true;
            }

            while (SpriteQueue.Count > 0)
            {
                yield return LoadWait;
                var loader = SpriteQueue.Dequeue();
                loader?.Invoke();
            }

            CoroutineRunning = false;
            if (SpriteQueue.Count > 0)
                SharedCoroutineStarter.instance.StartCoroutine(SpriteLoadCoroutine());
        }
    }

    public enum DownloadState { None, Downloading, DownloadedPlaylist, Downloaded, Error };
}
