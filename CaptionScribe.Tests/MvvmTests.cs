using System;
using System.Collections.Generic;
using CaptionScribe.Core.Mvvm;
using Xunit;

namespace CaptionScribe.Tests
{
    public class ObservableObjectTests
    {
        private sealed class Sample : ObservableObject
        {
            private int _number;
            public int Number { get => _number; set => SetProperty(ref _number, value); }

            private string? _text;
            public string? Text { get => _text; set => SetProperty(ref _text, value); }
        }

        [Fact]
        public void SetProperty_RaisesPropertyChanged_AndReturnsTrue_WhenValueChanges()
        {
            var sample = new Sample();
            var changed = new List<string?>();
            sample.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            sample.Number = 5;

            Assert.Equal(5, sample.Number);
            Assert.Equal(new[] { nameof(Sample.Number) }, changed);
        }

        [Fact]
        public void SetProperty_DoesNotRaise_WhenValueUnchanged()
        {
            var sample = new Sample { Number = 5 };
            int raises = 0;
            sample.PropertyChanged += (_, _) => raises++;

            sample.Number = 5;   // same value

            Assert.Equal(0, raises);
        }

        [Fact]
        public void SetProperty_HandlesReferenceTypesAndNullTransitions()
        {
            var sample = new Sample();
            int raises = 0;
            sample.PropertyChanged += (_, _) => raises++;

            sample.Text = "hi";   // null -> value: change
            sample.Text = "hi";   // same: no change
            sample.Text = null;   // value -> null: change

            Assert.Equal(2, raises);
            Assert.Null(sample.Text);
        }
    }

    public class RelayCommandTests
    {
        [Fact]
        public void CanExecute_IsTrue_WhenNoPredicateGiven()
        {
            var cmd = new RelayCommand(() => { });
            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void CanExecute_DelegatesToThePredicate()
        {
            bool allowed = false;
            var cmd = new RelayCommand(() => { }, () => allowed);

            Assert.False(cmd.CanExecute(null));
            allowed = true;
            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void Execute_InvokesTheAction()
        {
            int calls = 0;
            var cmd = new RelayCommand(() => calls++);

            cmd.Execute(null);

            Assert.Equal(1, calls);
        }

        [Fact]
        public void Constructor_Throws_WhenExecuteIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new RelayCommand(null!));
        }
    }
}
