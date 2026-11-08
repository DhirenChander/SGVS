# Running Tests

From the process directory run:

- cmake -S . -B build
- cmake --build build
- ctest --test-dir build -C Debug --output-on-failure
