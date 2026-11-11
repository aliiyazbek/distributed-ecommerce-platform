
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: 1,
  iterations: 1,
};

export default function () {
  const response = http.get('http://localhost:5169/swagger/index.html');

  check(response, {
    'status is 200': (r) => r.status === 200,
  });

  sleep(1);
}
