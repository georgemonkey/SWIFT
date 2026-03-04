from machine import UART
import usb_cdc 

# UART connected to your FC
uart = UART(0, baudrate=115200, tx=0, rx=1)  # adjust pins if needed

# USB serial (CDC)
usb = usb_cdc.data  # gives a serial interface over USB

while True:
    if uart.any():
        usb.write(uart.read())
    if usb.any():
        uart.write(usb.read())