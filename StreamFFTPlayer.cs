using FrooxEngine;
using CodeX;

namespace FourierForge;
public class StreamFFTPlayer<T> : StreamFFTBase, IStreamFFTPlayer
{
    public ImplicitStream[] FFTStreams { get => FFTVals; }
    public ValueStream<T>[] FFTVals { get; private set; }
    public ValueStream<float>[] BandMonitors{ get; private set; }
    public readonly UserAudioStream<StereoSample> AudioStream;
    private List<int>[] _bandSamples;
    public StreamFFTPlayer(UserAudioStream<StereoSample> audioStream, int binSize, int snipTo, int channels = 1) : base(binSize, snipTo, channels)
    {
        AudioStream = audioStream;
        FFTVals = StreamManipulator<T>.SetupStreams(audioStream, snipTo);
        _bandSamples = new List<int>[7];
        BandMonitors = setupBandMonitors();
    }
    private ValueStream<float>[] setupBandMonitors()
    {
        var bandStreams = new ValueStream<float>[7];
        Slot BandMonitorSlot = AudioStream.Slot.AddSlot("<color=purple>Band Monitors</color>");
        for (int i = 0; i < 7; i++)
        {
            var stream = AudioStream.User.GetStreamOrAdd<ValueStream<float>>($"FFTStreamBandMonitor.{AudioStream.ReferenceID}.{i}", stream => {
                stream.SetInterpolation();
                stream.SetUpdatePeriod(0, 0);
                ((Sync<float>)stream.GetSyncMember("InterpolationOffset")).Value = FourierForge.Config!.GetValue(FourierForge.InterpolationOffset);
                stream.Encoding = ValueEncoding.Full;
            });
            bandStreams[i] = stream;
            var monitorVar = BandMonitorSlot.AttachComponent<DynamicValueVariable<float>>();
            var driver = BandMonitorSlot.AttachComponent<ValueDriver<float>>();

            monitorVar.VariableName.Value = $"FFTBand{i}";
            driver.ValueSource.Target = stream;
            driver.DriveTarget.Target = monitorVar.Value;
        }
        
        float[] ranges = new float[8] { 20, 60, 250, 500, 2000, 4000, 6000, 20000 };
        
        int rangeIndex = 0;
        int sampleRate = Engine.Current.InputInterface.DefaultAudioInput.SampleRate;
        int binSize = this.FFTBinSize;

        for (int i = 0; i < this.FFTBinSize / 2; i++)
        {
            float freq = i * sampleRate / binSize;
            if (freq > 20000)
            {
                break;
            }
            if (freq >= ranges[rangeIndex + 1])
            {
                rangeIndex++;
            }
            if (_bandSamples[rangeIndex] == null)
            {
                _bandSamples[rangeIndex] = new List<int>();
            }
            _bandSamples[rangeIndex].Add(i);
        }
        return bandStreams;
    }
    private void applyBandMonitors()
    {
        for (int i = 0; i < 7; i++)
        {
            // convert each sample to decibels
            /*
            float avg = 0;
            for (int j = 0; j < _bandSamples[i].Count; j++)
            {
                avg += (float)(20 * Math.Log10(Math.Abs(SampleBuffer[_bandSamples[i][j]])));
            }
            avg /= _bandSamples[i].Count;
            */
            float avg = (float)_bandSamples[i].Max(j => SampleBuffer[j]);
            BandMonitors[i].Value = avg;
            BandMonitors[i].ForceUpdate();
        }
    }
    public void ApplyStreams(bool forceUpdate = false, bool onlyApplyBandMonitors = false)
    {
        applyBandMonitors();
        if (onlyApplyBandMonitors)
        {
            return;
        }
        StreamManipulator<T>.Apply(this, forceUpdate);
    }
    public void ExplodeStreams(Slot target)
    {
        for (int i = 0; i < this.FFTStreams.Count(); i++)
        {
            target.CreateReferenceVariable($"FFTVal{i}", this.FFTStreams[i]);
        }
        StreamManipulator<T>.ExplodeStreams(AudioStream, target);
    }
    public void ModifyStreamProperties(StreamProperties props)
    {
        AudioStream.RunSynchronously(() => {
            foreach (var val in FFTVals)
            {
                val.Encoding = props.Encoding;
                ((Sync<float>)val.GetSyncMember("InterpolationOffset")).Value = props.InterpolationOffset;
                val.FullFrameBits = props.FullFrameBits;
                val.SetUpdatePeriod(props.UpdatePeriod, props.UpdatePhase);
                var Stream = AudioStream.Stream.Target;
                
                if (Stream is OpusStream<StereoSample>)
                    ((OpusStream<StereoSample>)Stream).ApplicationType.Value = props.AppType;
                
            }
            _fftProvider.WindowFunction = WindowFunctionsList.WindowFuncs[(int)props.Window];
        });
    }
}