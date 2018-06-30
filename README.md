# Hyper-V Disk Activity
Shows Hyper-V virtual disk activity stats.

![Screenshot](https://raw.githubusercontent.com/wiki/rstarkov/HyperVDiskUsage/screenshot1.png)

Samples disk queue length for every physical and virtual disk 5 times a second. Any time the queue length is found to be
non-zero, the disk is considered busy. Any time the queue length exceeds 1, it is considered "behind". This is what's shown
in the "Busy" and "Behind" percentage columns.

For physical disks, Windows also provides a performance counter which maintains a more accurate average queue length. When
the disk activity is low, the average queue length × 100 is approximately equal to busy percentage.
