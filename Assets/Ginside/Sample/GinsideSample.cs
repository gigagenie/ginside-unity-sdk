/*
 * Copyright 2022 kt corp.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */

namespace Ginside.Sample
{
    using Ginside.Extensions;
    using Ginside.Model;
    using System;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.Networking;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using UnityEngine.UIElements;
    // using RenderHeads.Media.AVProVideo;

    public class GinsideSample : MonoBehaviour
    {
        private string id;
        private string key;
        private string secret;

        // public MediaPlayer mediaPlayer;

        [SerializeField]
        public VisualTreeAsset ListEntryTemplate;

        private TextField sttTextField;
        private Button recordButton;
        private Button sendButton;
        private GameObject ttsPlayer;

        private Ginside.GinsideApis lazy;
        private Ginside.GinsideApis ginsideApis
        {
            get
            {
                if (lazy == null)
                {
                    lazy = Ginside.GinsideApis.CreateInstance(id, key, secret);
                    lazy.SetConfig("{\"devMode\":true}");
                    lazy.SetConfig("{\"locationInfo\": {\"latitude\": 37.47156,\"longitude\": 127.02933,\"address\": \"경기도 성남시 분당구 불정로 90\"}}");
                    lazy.SetConfig($"{{\"deviceType\":\"{Ginside.Model.DeviceType.ALL.ToString()}\"}}");
                    // lazy.SetConfig($"{{\"ttsSpeaker\":\"man1\"}}");
                    // lazy.SetConfig($"{{\"ttsSpeed\":0}}");
                    // lazy.SetConfig($"{{\"retries\":[]}}"); // remove retry policy
                    // lazy.SetConfig($"{{\"retries\":[3000, 5000]}}"); // set 3, 5 seconds retry policy

                    lazy.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName.Equals("CurrentMedia"))
                        {
                            SetSelected(programList, lazy.CurrentMedia);
                            if (!String.IsNullOrEmpty(lazy.CurrentMedia?.channelId))
                            {
                                SetSelected(channelList, Media.Builder.Create(Media.TYPE_CHANNEL).Id(lazy.CurrentMedia.channelId).Build());
                            }
                        }
                        else if (e.PropertyName.Equals("CurrentMediaList"))
                        {
                            try
                            {
                                BindList(channelList, lazy.CurrentMediaList.FindAll((e) => { return e.type.Equals(Media.TYPE_CHANNEL); }));
                                BindList(programList, lazy.CurrentMediaList.FindAll((e) => { return !e.type.Equals(Media.TYPE_CHANNEL); }));
                            }
                            catch (Exception exp)
                            {
                                Debug.Log(exp);
                            }
                        }
                    };

                }
                return lazy;
            }
        }

        private AudioSource currentMedia;
        private AudioSource currentTts;

        private bool _isRecordingAudio;
        private bool IsRecordingAudio
        {
            get
            {
                return _isRecordingAudio;
            }
            set
            {
                if (value)
                {
                    recordButton.AddToClassList("record");
                }
                else
                {
                    recordButton.RemoveFromClassList("record");
                }
                _isRecordingAudio = value;
            }
        }
        private string input
        {
            get
            {
                return Microphone.devices[0].ToString();
            }
        }

        private ListView chatList;
        private ListView channelList;
        private ListView programList;

        private byte[] AudioClip2Int16Bytes(float[] data)
        {
            MemoryStream dataStream = new MemoryStream();

            for (int i = 0; i < data.Length; i++)
            {
                dataStream.Write(BitConverter.GetBytes(Convert.ToInt16(data[i] * Int16.MaxValue)), 0, sizeof(Int16));
            }
            byte[] bytes = dataStream.ToArray();

            dataStream.Dispose();

            return bytes;
        }

        private void SetSelected(ListView view, Media media)
        {
            if (media == null || view.itemsSource == null)
            {
                view.SetSelectionWithoutNotify(new List<int>());
                return;
            }
            for (int i = 0; i < view.itemsSource.Count; i++)
            {
                if (media.Equals(view.itemsSource[i]))
                {
                    view.SetSelectionWithoutNotify(new List<int> { i });
                    break;
                }
            }
        }

        private void InitBindList(ListView view)
        {
            view.makeItem = () =>
            {
                var newListEntry = ListEntryTemplate.Instantiate();
                newListEntry.AddToClassList("media-container");
                Action<string> newListEntryLogic = (text) =>
                {
                    Label label = newListEntry.Q<Label>();
                    if (label != null)
                    {
                        label.AddToClassList("media");
                        label.text = text;
                    }
                };

                newListEntry.userData = newListEntryLogic;

                return newListEntry;
            };
            view.destroyItem = (item) =>
            {
                item.userData = null;
            };
            view.bindItem = (item, index) =>
            {
                var newListEntryLogic = item.userData as Action<string>;
                if (newListEntryLogic != null && index < view.itemsSource.Count)
                {
                    var media = view.itemsSource[index] as Media;
                    newListEntryLogic(media?.title != null ? media?.title : media?.channelTitle);
                }
            };
            view.onSelectionChange += async (obj) =>
            {
                var media = view.selectedItem as Media;
                var mediaCommand = await ginsideApis.PlayMedia(media);

                Debug.Log($"play title:{mediaCommand?.media?.title}, url:{mediaCommand?.media?.streamUrl}");
                media = mediaCommand.media;
                UnityWebRequest requestMultimedia = UnityWebRequestMultimedia.GetAudioClip(media.streamUrl, AudioType.MPEG);
                if (media.channelId != null && media.channelId.Equals("cbs"))
                {
                    requestMultimedia.certificateHandler = new TrustAll();
                }
                await requestMultimedia.SendWebRequest();
                playMediaClip(DownloadHandlerAudioClip.GetContent(requestMultimedia));
            }; ;

            view.fixedItemHeight = 45;
        }

        private void InitChatList(ListView view)
        {
            view.makeItem = () =>
            {
                var newListEntry = ListEntryTemplate.Instantiate();
                Action<string, string> newListEntryLogic = (className, text) =>
                {
                    newListEntry.AddToClassList(className);
                    Label label = newListEntry.Q<Label>();
                    if (label != null)
                    {
                        label.AddToClassList("chat");
                        label.text = text;
                    }
                };

                newListEntry.userData = newListEntryLogic;

                return newListEntry;
            };
            view.destroyItem = (item) =>
            {
                item.userData = null;
            };
            view.bindItem = (item, index) =>
            {
                var newListEntryLogic = item.userData as Action<string, string>;
                if (newListEntryLogic != null)
                {
                    var tuple = view.itemsSource[index] as Tuple<string, string>;
                    newListEntryLogic(tuple.Item1, tuple.Item2);
                }
            };
            view.selectionType = SelectionType.None;
            view.itemsSource = new List<Tuple<string, string>>();
            view.fixedItemHeight = 40;
        }

        void BindList(ListView view, List<Media> list)
        {
            view.Clear();
            view.itemsSource = list;
            view.RefreshItems();
        }
        void OnEnable()
        {
            var uiDocument = GetComponent<UIDocument>() as UIDocument;
            var root = uiDocument.rootVisualElement;

            recordButton = root.Q<Button>("record");
            sendButton = root.Q<Button>("send");
            sttTextField = root.Q<TextField>();
            channelList = root.Q<ListView>("channel-list");
            programList = root.Q<ListView>("program-list");
            chatList = root.Q<ListView>("chat-list");
            InitBindList(channelList);
            InitBindList(programList);
            InitChatList(chatList);

            recordButton.RegisterCallback<ClickEvent>(OnClick);
            sendButton.RegisterCallback<ClickEvent>(onInput);
            sttTextField.RegisterCallback<KeyDownEvent>(onKeyDown);
        }

        void Start()
        {
            ttsPlayer = new GameObject("Audio");
            currentMedia = ttsPlayer.AddComponent<AudioSource>();
            currentTts = ttsPlayer.AddComponent<AudioSource>();
        }

        IEnumerator Pumping(AudioClip clip, Stream stream, int timeout)
        {
            var input = Microphone.devices[0].ToString();
            var lastPos = 0;
            var pos = 0;
            var total = 0;

            while (Microphone.IsRecording(input) == true)
            {
                if ((pos = Microphone.GetPosition(input)) > 0)
                {
                    if (lastPos > pos)
                    {
                        lastPos = 0;
                    }

                    if (pos - lastPos > 0)
                    {
                        float[] data = new float[(pos - lastPos) * clip.channels];

                        clip.GetData(data, lastPos);

                        for (int i = 0; i < data.Length; i++)
                        {
                            stream.Write(BitConverter.GetBytes(Convert.ToInt16(data[i] * Int16.MaxValue)), 0, sizeof(Int16));
                        }
                        total += data.Length;

                        lastPos = pos;
                    }
                }

                yield return null;
            }
        }
        async void OnClick(ClickEvent evt)
        {
            if (!IsRecordingAudio)
            {
                Stream stream = Stream.Synchronized(new MemoryStream());
                int timeout = 10000; // 10 seconds
                int tailSilence = 700; // 0.7 seconds
                double threshold = 0.9; // 90% of energy 
                Task<Command> commandTask = ginsideApis.SendStreamVoice(stream, timeout, tailSilence, threshold);

                AudioClip clip = Microphone.Start(Microphone.devices[0].ToString(), false, timeout / 1000, 16000);
                StartCoroutine(Pumping(clip, stream, timeout));
                IsRecordingAudio = true;

                Command command = await commandTask;

                IsRecordingAudio = false;
                stream.Dispose();
                Microphone.End(Microphone.devices[0].ToString());

                HandleCommand(command);
            }
        }

        private int last = 0;
        async void onKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return)
            {
                var current = Interlocked.Increment(ref last);
                await Task.Delay(30);
                if (current == last) {
                    onInput(null);
                }
            }
        }

        async void onInput(ClickEvent evt)
        {
            var text = sttTextField.value;
            var command = await ginsideApis.SendText(text);
            HandleCommand(command);
        }

        async void HandleCommand(Command command)
        {
            // draw UI
            if (!String.IsNullOrEmpty(command?.uword))
            {
                chatList.itemsSource.Add(new Tuple<string, string>("user-chat-container", command.uword));
            }
            if (!String.IsNullOrEmpty(command?.ttsText))
            {
                chatList.itemsSource.Add(new Tuple<string, string>("tts-chat-container", command.ttsText));
            }
            chatList.Rebuild();

            // play TTS
            Ginside.Model.Wav wav = command.ttsWav;
            if (wav != null)
            {
                var audioClip = AudioClip.Create("tts", wav.SampleCount, wav.ChannelCount, wav.Frequency, false);
                audioClip.SetData(wav.LeftChannel, 0);
                playTtsClip(audioClip);
            }

            // play Media
            Ginside.Model.Media media = command.media;
            if (media != null)
            {
                Command mediaCommand = await ginsideApis.PlayMedia(media);

                // play mediaCommand.media
                Debug.Log($"play title:{mediaCommand?.media?.title}, url:{mediaCommand?.media?.streamUrl}");
                media = mediaCommand.media;
                UnityWebRequest requestMultimedia = UnityWebRequestMultimedia.GetAudioClip(media.streamUrl, AudioType.MPEG);
                if (media.channelId != null && media.channelId.Equals("cbs"))
                {
                    requestMultimedia.certificateHandler = new TrustAll();
                }

                await requestMultimedia.SendWebRequest();
                playMediaClip(DownloadHandlerAudioClip.GetContent(requestMultimedia));
            }

            if (media == null && ginsideApis.CurrentMedia == null)
            {
                playMediaClip(null);
            }

            Debug.Log($"content: {command.content}");
        }

        void playMediaClip(AudioClip mediaClip)
        {
            currentMedia.Stop();
            if (mediaClip != null)
            {
                currentMedia.clip = mediaClip;
                currentMedia.volume = 1.0f;
                currentMedia.Play();
            }
        }

        void playTtsClip(AudioClip ttsClip)
        {
            if (currentMedia.isPlaying)
            {
                currentMedia.Pause();
                StartCoroutine(ResumeMedia(ttsClip.length));
            }

            currentTts.Stop();
            currentTts.clip = ttsClip;
            currentTts.volume = 1.0f;
            currentTts.Play();
        }

        IEnumerator ResumeMedia(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            currentMedia.UnPause();
        }
    }

    public class TrustAll : CertificateHandler
    {
        override protected bool ValidateCertificate(byte[] certificateData)
        {
            return true;
        }
    }
}
