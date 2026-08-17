#!/usr/bin/env python3
"""Native round-trip/compatibility check for the correlated asset-loaded ACK."""

import ctypes
import pathlib
import sys


class AssetLoaded(ctypes.Structure):
    _fields_ = [
        ("asset_type", ctypes.c_void_p),
        ("name", ctypes.c_void_p),
        ("context", ctypes.c_void_p),
    ]


class AssetLoadRequest(ctypes.Structure):
    _fields_ = [
        ("asset_type", ctypes.c_void_p),
        ("name", ctypes.c_void_p),
        ("context", ctypes.c_void_p),
        ("url", ctypes.c_void_p),
    ]


def cptr(value: bytes):
    buffer = ctypes.create_string_buffer(value)
    return buffer, ctypes.cast(buffer, ctypes.c_void_p)


def main() -> int:
    default = pathlib.Path(__file__).resolve().parent / "build-native/libCoreSdkDll.so"
    library_path = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else default
    sdk = ctypes.CDLL(str(library_path))

    sdk.PB_EXP_AssetLoadedAck_Serialize.argtypes = [ctypes.POINTER(AssetLoaded), ctypes.POINTER(ctypes.c_int)]
    sdk.PB_EXP_AssetLoadedAck_Serialize.restype = ctypes.c_void_p
    sdk.PB_EXP_AssetLoadedAck_Deserialize.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(AssetLoaded)]
    sdk.PB_EXP_AssetLoadedAck_Deserialize.restype = ctypes.c_bool
    sdk.PB_EXP_AssetLoadedAck_Free.argtypes = [ctypes.POINTER(AssetLoaded)]
    sdk.PB_EXP_AssetLoadRequestOp_Serialize.argtypes = [ctypes.POINTER(AssetLoadRequest), ctypes.POINTER(ctypes.c_int)]
    sdk.PB_EXP_AssetLoadRequestOp_Serialize.restype = ctypes.c_void_p
    sdk.PB_EXP_Free.argtypes = [ctypes.c_void_p]

    keepalive = [cptr(value) for value in (b"UnityPrefab", b"MentalFacility", b"unityclient")]
    source = AssetLoaded(*(pointer.value for _, pointer in keepalive))
    length = ctypes.c_int()
    payload = sdk.PB_EXP_AssetLoadedAck_Serialize(ctypes.byref(source), ctypes.byref(length))
    assert payload and length.value > ctypes.sizeof(ctypes.c_void_p)
    try:
        parsed = AssetLoaded()
        assert sdk.PB_EXP_AssetLoadedAck_Deserialize(payload, length.value, ctypes.byref(parsed))
        try:
            assert ctypes.string_at(parsed.asset_type) == b"UnityPrefab"
            assert ctypes.string_at(parsed.name) == b"MentalFacility"
            assert ctypes.string_at(parsed.context) == b"unityclient"
        finally:
            sdk.PB_EXP_AssetLoadedAck_Free(ctypes.byref(parsed))
    finally:
        sdk.PB_EXP_Free(payload)

    # The historic response is exactly one pointer wide and contains no
    # strings. It must retain spawn-chain compatibility without ever being
    # accepted as a correlated response.
    legacy = (ctypes.c_ubyte * ctypes.sizeof(ctypes.c_void_p))(*range(ctypes.sizeof(ctypes.c_void_p)))
    rejected = AssetLoaded()
    assert not sdk.PB_EXP_AssetLoadedAck_Deserialize(legacy, len(legacy), ctypes.byref(rejected))

    # A normal AssetLoadRequest protobuf has the same field shape but no v1
    # marker. That must not release a pending runtime terrain request either.
    request = AssetLoadRequest(*(pointer.value for _, pointer in keepalive), None)
    request_len = ctypes.c_int()
    request_payload = sdk.PB_EXP_AssetLoadRequestOp_Serialize(ctypes.byref(request), ctypes.byref(request_len))
    assert request_payload
    try:
        assert not sdk.PB_EXP_AssetLoadedAck_Deserialize(
            request_payload, request_len.value, ctypes.byref(rejected))
    finally:
        sdk.PB_EXP_Free(request_payload)

    print("correlated asset-loaded ACK: round-trip exact; legacy/unmarked rejected")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
