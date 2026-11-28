namespace SGVS.Frontend.Services.Video;

// the pieces NetworkMeetingService needs to send and show video. the real ones are made in
// App.CreateMeeting, tests hand in fakes
// StallTimeout: how long your encoder may go without sending video before the stream is stopped, 5 s if null
public sealed record VideoServices(Func<IStreamPublisher> CreatePublisher, Func<IVideoDecoder> CreateDecoder,
    TimeSpan? StallTimeout = null);
