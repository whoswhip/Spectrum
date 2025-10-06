#include <Mouse.h>

enum ParseState : uint8_t { WAIT_CMD, WAIT_MOVE_X, WAIT_MOVE_Y };
ParseState state = WAIT_CMD;
uint8_t cmd = 0;
int8_t moveX = 0, moveY = 0;

void setup() {
  Serial.begin(115200);
  delay(2000);
  Mouse.begin();
}

void loop() {
  while (Serial.available() > 0) {
    int b = Serial.read();
    if (b < 0) return;

    switch (state) {
      case WAIT_CMD:
        cmd = (uint8_t)b;
        switch (cmd) {
          case 0x01: // move: need two more bytes
            state = WAIT_MOVE_X;
            break;

          case 0x02: 
            Serial.write(0xAA); // ping
            break;

          case 0x03: // left down
            Mouse.press(MOUSE_LEFT);
            break;

          case 0x04: // left up
            Mouse.release(MOUSE_LEFT);
            break;

          case 0x05: // right down
            Mouse.press(MOUSE_RIGHT);
            break;

          case 0x06: // right up
            Mouse.release(MOUSE_RIGHT);
            break;

          default:
            break;
        }
        break;

      case WAIT_MOVE_X:
        moveX = (int8_t)b;
        state = WAIT_MOVE_Y;
        break;

      case WAIT_MOVE_Y:
        moveY = (int8_t)b;
        Mouse.move(moveX, moveY, 0);
        state = WAIT_CMD;
        break;
    }
  }
}
