using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Linq;

namespace musicApp
{
    public class Playlist : INotifyPropertyChanged
    {
        private bool _isPinned;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        
        [JsonIgnore]
        public ObservableCollection<Song> Tracks { get; set; } = new ObservableCollection<Song>();
        
        // For serialization - we'll store the file paths and reconstruct the tracks
        public List<string> TrackFilePaths { get; set; } = new List<string>();
        
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime LastModified { get; set; } = DateTime.Now;

        /// <summary>
        /// Whether the playlist is pinned (starred) in the list. Default is false.
        /// </summary>
        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned == value) return;
                _isPinned = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public Playlist()
        {
        }

        public Playlist(string name, string description = "")
        {
            Name = name;
            Description = description;
        }

        public void AddTrack(Song track)
        {
            if (!Tracks.Any(t => t.FilePath == track.FilePath))
            {
                Tracks.Add(track);
                TrackFilePaths.Add(track.FilePath);
                LastModified = DateTime.Now;
            }
        }

        public void RemoveTrack(Song track)
        {
            Tracks.Remove(track);
            TrackFilePaths.Remove(track.FilePath);
            LastModified = DateTime.Now;
        }

        public void Clear()
        {
            Tracks.Clear();
            TrackFilePaths.Clear();
            LastModified = DateTime.Now;
        }

        // Method to reconstruct tracks from file paths
        public void ReconstructTracks(IEnumerable<Song> availableTracks)
        {
            Tracks.Clear();
            foreach (var filePath in TrackFilePaths)
            {
                var track = availableTracks.FirstOrDefault(t => t.FilePath == filePath);
                if (track != null)
                {
                    Tracks.Add(track);
                }
            }
        }
    }
} 