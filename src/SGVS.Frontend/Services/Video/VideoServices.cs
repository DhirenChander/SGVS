namespace SGVS.Frontend.Services.Video;

// the pieces NetworkMeetingService needs to send and show video. the real ones are made in
// MainViewModel.CreateMeeting, tests hand in fakes
public sealed record VideoServices(Func<IStreamPublisher> CreatePublisher, Func<IVideoDecoder> CreateDecoder);
