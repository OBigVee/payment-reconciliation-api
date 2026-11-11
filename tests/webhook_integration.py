"""Exercise the running API and its PostgreSQL database over HTTP."""

import json
import os
import unittest
from concurrent.futures import ThreadPoolExecutor
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from uuid import uuid4


BASE_URL = os.getenv("PAYMENT_API_URL", "http://localhost:5080")


def request(path, body=None):
    data = None if body is None else json.dumps(body).encode()
    http_request = Request(BASE_URL + path, data=data, headers={"Content-Type": "application/json"})
    try:
        response = urlopen(http_request, timeout=10)
    except HTTPError as error:
        response = error
    with response:
        return response.code, json.load(response)


class ProviderWebhookTests(unittest.TestCase):
    def create_payment(self):
        status, payment = request("/payments", {"amountMinor": 2500, "currency": "NGN"})
        self.assertEqual(status, 201)
        self.assertEqual(payment["status"], "pending")
        return payment["id"]

    def test_applies_event_and_ignores_replay(self):
        payment_id = self.create_payment()
        event = {"eventId": str(uuid4()), "paymentId": payment_id, "status": "succeeded"}

        self.assertEqual(request("/webhooks/provider", event), (200, {"applied": True}))
        self.assertEqual(request(f"/payments/{payment_id}")[1]["status"], "succeeded")
        self.assertEqual(request("/webhooks/provider", event), (200, {"applied": False}))
        self.assertEqual(request(f"/payments/{payment_id}")[1]["status"], "succeeded")

    def test_concurrent_duplicate_is_applied_once(self):
        payment_id = self.create_payment()
        event = {"eventId": str(uuid4()), "paymentId": payment_id, "status": "failed"}
        with ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(lambda _: request("/webhooks/provider", event), range(2)))
        self.assertCountEqual(results, [(200, {"applied": True}), (200, {"applied": False})])
        self.assertEqual(request(f"/payments/{payment_id}")[1]["status"], "failed")

    def test_conflicting_payloads_preserve_final_status(self):
        payment_id = self.create_payment()
        event = {"eventId": str(uuid4()), "paymentId": payment_id, "status": "succeeded"}
        self.assertEqual(request("/webhooks/provider", event)[0], 200)
        self.assertEqual(request("/webhooks/provider", {**event, "status": "failed"})[0], 409)
        self.assertEqual(request("/webhooks/provider", {**event, "eventId": str(uuid4())})[0], 409)
        self.assertEqual(request(f"/payments/{payment_id}")[1]["status"], "succeeded")

    def test_unknown_payment_and_invalid_status(self):
        event = {"eventId": str(uuid4()), "paymentId": str(uuid4()), "status": "succeeded"}
        self.assertEqual(request("/webhooks/provider", event)[0], 404)
        self.assertEqual(request("/webhooks/provider", {**event, "status": "pending"})[0], 400)


if __name__ == "__main__":
    unittest.main(verbosity=2)
