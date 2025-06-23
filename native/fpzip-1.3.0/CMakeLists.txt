# Placeholder CMakeLists.txt for building fpzip C++ utility
# In a real scenario, this would be the actual CMakeLists.txt from fpzip 1.3.0 source.

cmake_minimum_required(VERSION 3.10)
project(fpzip_native CXX C)

# Add include directories
include_directories(include)

# Add library sources
add_library(fpzip_lib src/placeholder_lib.cpp)
target_include_directories(fpzip_lib PUBLIC include)


# Add executable sources (the command line utility)
add_executable(fpzip_cli utils/fpzip.cpp)
target_link_libraries(fpzip_cli fpzip_lib)

# Install rules (optional, but good practice)
install(TARGETS fpzip_cli DESTINATION bin)
install(TARGETS fpzip_lib DESTINATION lib)
install(FILES include/fpzip.h DESTINATION include)

# --- Minimal source file placeholders to make this conceptual CMake work ---
# Create dummy files that would be referenced, e.g.:
# file(WRITE include/fpzip.h "#pragma once\n // Dummy fpzip.h")
# file(WRITE src/read.cpp "// Dummy read.cpp\n#include \"fpzip.h\"")
# file(WRITE src/write.cpp "// Dummy write.cpp\n#include \"fpzip.h\"")
# file(WRITE src/error.cpp "// Dummy error.cpp\n#include \"fpzip.h\"")
# file(WRITE src/version.cpp "// Dummy version.cpp\n#include \"fpzip.h\"")
# file(WRITE utils/fpzip.cpp "// Dummy fpzip.cpp CLI utility\n#include \"fpzip.h\"\nint main() { return 0; }")
# --- End of dummy file creation ---

# The actual fpzip CMakeLists.txt is more complex and handles different FP modes, options etc.
# This placeholder is just to illustrate the build step in the workflow.
# For the workflow to actually compile, it would need the real fpzip 1.3.0 source code here.
# For now, the workflow will attempt to run this CMake file.
# To make it runnable as a placeholder, I'll add dummy source files.
# (Agent note: I will create these dummy files in subsequent steps)

# Set C++ standard if required by fpzip (e.g., C++98 or C++11)
set(CMAKE_CXX_STANDARD 98)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Define FPZIP_FP_SAFE (or other modes as needed by the benchmark)
# This is typically done via compiler definitions or by modifying Config/CMake options
# For FP_SAFE, fpzip.h usually defaults or it's set in a config file.
# Let's assume for this placeholder, FPZIP_FP is set to FPZIP_FP_SAFE (2)
# This would normally be handled by fpzip's own build system (e.g. in its Config or CMake options)
add_compile_definitions(FPZIP_FP=2) # Corresponds to FPZIP_FP_SAFE
add_compile_definitions(FPZIP_FP_SAFE) # Ensure this is also defined if headers check it
# The original fpzip has a 'Config' file that sets these, or CMake options.
# For this placeholder, we define it directly.
# The real fpzip source would have these definitions managed by its build scripts.
# This definition might also go into a toolchain file or be passed as a CMake argument.

# If fpzip has specific compile options for release (optimizations)
set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type (Debug, Release, etc.)")
set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} -O3") # Example optimization
set(CMAKE_C_FLAGS_RELEASE "${CMAKE_C_FLAGS_RELEASE} -O3")
