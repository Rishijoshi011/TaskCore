using System;
using System.ComponentModel;

namespace TaskCore.Models
{
    public class ProcessInfo : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public int Id { get; set; }

        private float _memory;
        public float Memory
        {
            get => _memory;
            set
            {
                if (Math.Abs(_memory - value) > 0.1f)
                {
                    _memory = value;
                    OnPropertyChanged(nameof(Memory));
                }
            }
        }

        private float _cpu;
        public float Cpu
        {
            get => _cpu;
            set
            {
                if (Math.Abs(_cpu - value) > 0.1f)
                {
                    _cpu = value;
                    OnPropertyChanged(nameof(Cpu));
                }
            }
        }

        // 🔹 For grouping
        public string GroupName => Name;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
