# HP Agilent Keysight 34401A Control and Data Logging Software
 This software allows you to control and log data from the HP Agilent Keysight 34401A multimeter. This software supports Windows 10, 7, 8, and 8.1. Windows Server 2019, 2016 and 2012 are also supported.

 Connect via Serial Port: You will need a USB to Serial Adapter as well as a Null Modem Adapter/Cable if you wish to connect via the 34401A's serial port. 

 Connect via GPIB Port: You will need an AR488 GPIB Arduino Adapter if you wish to connect via the 34401A's GPIB port. 

#### [Download]()

#### [User Manual]()

#### [AR488 Adapter](https://github.com/Twilight-Logic/AR488)


#### The main software window
![HP 34401A Software]()

#### Interactive Graphing Module
![HP 34401A Graph Module]()

**Features:**

- Control and Log data, save data into organized folders
- Multithreading Support:
   - All Windows open in a new thread, this ensures maximum performance. For example: Interacting with the Graph Window does not slow down other Windows.
    - All Serial communication happens on an exclusive thread. This allows the software to always maintain maximum sample capture speed, regardless of what other data processing might be going on.
    - Users can interact with the Graph Window smoothly without any lag.
    - Data Logging functions also run on their designated thread, periodically the software will save measurement data from FIFO data structures into text files.
- Speech Synthesizer feature allows the software to voice measurements periodically and or when it meets the maximum or minimum value threshold.
- Graph Window allows users to visualize their captured data. You can get statistics for all the samples capture or for select few samples. Pan, zoom, and zoom to highlighted area. Save/copy graph as image or save graph's data into text/csv files. 
- Create math waveforms from the samples captured. Create math waveforms from math waveforms. There is no limit to how many math waveforms you can create. 
- Create Histogram from the samples captured and from math waveforms. There is no limit to how many histogram waveforms you can create.
- Measurement table allows users to collect and display the measurement data into a table.
- Capture up to 280 measurement samples in 2 seconds (GPIB Port). Serial Port can only capture up to 60 measurement samples in 2 seconds.

#### Create Math Waveform
![HP 34401A  Math Waveform]()

#### Histogram Waveform
![HP 34401A Histogram]()

#### Graph Markers
![HP 34401A Markers]()

#### Measurement Table
![HP 34401A Table]()

#### Fast Measurement Display
![HP 34401A Table]()

#### Connect via AR488 Arduino GPIB Adapater
![HP 34401A Software]()