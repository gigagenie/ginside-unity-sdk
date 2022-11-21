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
    using UnityEngine.UI;
    using System.IO;
    using System.Threading.Tasks;
    // using RenderHeads.Media.AVProVideo;

    public class GinsideSample2020 : MonoBehaviour
    {
        public InputField sttTextField;
        public Button recordButton;
        private string id;
        private string key;
        private string secret;

        // public MediaPlayer mediaPlayer;
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
                            Debug.Log($"CurrentMedia: {lazy.CurrentMedia}");
                        }
                        else if (e.PropertyName.Equals("CurrentMediaList"))
                        {
                            for (int i = 0; i < lazy.CurrentMediaList.Count; i++)
                            {
                                Debug.Log($"CurrentMediaList[{i}]: {lazy.CurrentMediaList[i]}");
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

        void Start()
        {
            ttsPlayer = new GameObject("Audio");
            currentMedia = ttsPlayer.AddComponent<AudioSource>();
            currentTts = ttsPlayer.AddComponent<AudioSource>();
            sttTextField.onEndEdit.AddListener(onInput);
            recordButton.onClick.AddListener(OnClick);
            Debug.Log($"Start ttsPlayer:{ttsPlayer}");
        }


        IEnumerator Pumping(AudioClip clip, Stream stream, int timeout, string mic)
        {
            var lastPos = 0;
            var pos = 0;
            var total = 0;

            while (Microphone.IsRecording(mic) == true)
            {
                if ((pos = Microphone.GetPosition(mic)) > 0)
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
        async void OnClick()
        {
            if (!IsRecordingAudio)
            {
                Stream stream = Stream.Synchronized(new MemoryStream());
                int timeout = 10000; // 10 seconds
                int tailSilence = 700; // 0.7 seconds
                double threshold = 0.9; // 90% of energy 
                Task<Command> commandTask = ginsideApis.SendStreamVoice(stream, timeout, tailSilence, threshold);

                string mic = input;
                AudioClip clip = Microphone.Start(mic, false, timeout / 1000, 16000);
                StartCoroutine(Pumping(clip, stream, timeout, mic));
                StartCoroutine(Flash(mic));
                IsRecordingAudio = true;

                Command command = await commandTask;

                IsRecordingAudio = false;
                stream.Dispose();
                Microphone.End(mic);

                HandleCommand(command);
            }
        }

        async void onInput(string text)
        {
            var command = await ginsideApis.SendText(text);
            HandleCommand(command);
        }

        async void HandleCommand(Command command)
        {
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

        IEnumerator Flash(string mic)
        {
            var tileFlashSpeed = 4f;
            var material = transform.GetComponent<Renderer>().material;
            var initialColor = material.color;
            var flashColor = Color.red;

            var timer = 0f;
            while (Microphone.IsRecording(mic) == true)
            {
                material.color = Color.Lerp(initialColor, flashColor, Mathf.PingPong(timer * tileFlashSpeed, 1));
                timer += Time.deltaTime;
                yield return null;
            }
            material.color = initialColor;
        }

        public class TrustAll : CertificateHandler
        {
            override protected bool ValidateCertificate(byte[] certificateData)
            {
                return true;
            }
        }
    }
}
