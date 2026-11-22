import argparse
import sys


# Sends one ping to the running LiveScreenshot instance.
def main():
    parser = argparse.ArgumentParser(description="Ping a pixel in the current LiveScreenshot capture.")
    parser.add_argument("x", type=int)
    parser.add_argument("y", type=int)
    args = parser.parse_args()
    if not (0 <= args.x <= 2_147_483_647 and 0 <= args.y <= 2_147_483_647):
        parser.error("coordinates must be nonnegative 32-bit integers")

    try:
        with open(r"\\.\pipe\LiveScreenshot-Command", "r+b", buffering=0) as pipe:
            pipe.write(f"PING {args.x} {args.y}\n".encode("ascii"))
            reply = pipe.readline().decode("utf-8").rstrip("\r\n")
    except (OSError, UnicodeError) as error:
        print(f"LiveScreenshot command failed: {error}", file=sys.stderr)
        return 1

    if reply != "OK":
        print(reply or "LiveScreenshot closed the pipe without replying.", file=sys.stderr)
        return 1

    print(reply)
    return 0


if __name__ == "__main__":
    sys.exit(main())
