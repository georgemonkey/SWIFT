import machine
import utime

lidar = machine.UART(0, baudrate=230400, tx=machine.Pin(0), rx=machine.Pin(1))
machine.Pin(2, machine.Pin.OUT).value(1) # Spin motor

buf = bytearray(47)

while True:
    if lidar.any() >= 47:
        if lidar.read(1) == b'\x54':
            lidar.readinto(memoryview(buf)[1:])
            # Fast parse and print
            angle = (buf[4] | (buf[5] << 8)) / 100.0
            dist = buf[6] | (buf[7] << 8)
            if 0 < dist < 5000:
                print(f"{angle},{dist}")
    # NO SLEEP here for maximum possible speed