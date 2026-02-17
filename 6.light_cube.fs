#include <glad/glad.h>
#include <GLFW/glfw3.h>
#include <stb_image.h>

#include <glm/glm.hpp>
#include <glm/gtc/matrix_transform.hpp>
#include <glm/gtc/type_ptr.hpp>

#include <learnopengl/filesystem.h>
#include <learnopengl/shader_m.h>
#include <learnopengl/camera.h>

#include <iostream>
#include <vector>
#include <string>
#include <cmath>
#include <random>

// ---------- callbacks / utils ----------
void framebuffer_size_callback(GLFWwindow* window, int width, int height);
void mouse_callback(GLFWwindow* window, double xpos, double ypos);
void scroll_callback(GLFWwindow* window, double xoffset, double yoffset);
void processInput(GLFWwindow *window);
unsigned int loadTexture(const char *path);

// settings
const unsigned int SCR_WIDTH = 800;
const unsigned int SCR_HEIGHT = 600;

// camera
Camera camera(glm::vec3(0.0f, 0.0f, 6.0f));
float lastX = SCR_WIDTH / 2.0f;
float lastY = SCR_HEIGHT / 2.0f;
bool firstMouse = true;

// timing
float deltaTime = 0.0f;
float lastFrame = 0.0f;

// ---------------- Sphere mesh ----------------
struct SphereMesh {
    unsigned int VAO = 0, VBO = 0, EBO = 0;
    int indexCount = 0;
};

static SphereMesh buildSphere(float radius, int stacks, int sectors)
{
    SphereMesh m;
    std::vector<float> data; // pos(3), normal(3), uv(2)
    std::vector<unsigned int> idx;

    data.reserve((stacks + 1) * (sectors + 1) * 8);

    const float PI = 3.14159265359f;

    for (int i = 0; i <= stacks; i++) {
        float v = (float)i / (float)stacks;
        float phi = v * PI;                 // 0..pi
        float y = std::cos(phi);
        float r = std::sin(phi);

        for (int j = 0; j <= sectors; j++) {
            float u = (float)j / (float)sectors;
            float theta = u * (2.0f * PI);  // 0..2pi

            float x = r * std::cos(theta);
            float z = r * std::sin(theta);

            // position
            data.push_back(radius * x);
            data.push_back(radius * y);
            data.push_back(radius * z);
            // normal (unit)
            data.push_back(x);
            data.push_back(y);
            data.push_back(z);
            // uv
            data.push_back(u);
            data.push_back(v);
        }
    }

    for (int i = 0; i < stacks; i++) {
        for (int j = 0; j < sectors; j++) {
            int row1 = i * (sectors + 1);
            int row2 = (i + 1) * (sectors + 1);

            idx.push_back(row1 + j);
            idx.push_back(row2 + j);
            idx.push_back(row2 + j + 1);

            idx.push_back(row1 + j);
            idx.push_back(row2 + j + 1);
            idx.push_back(row1 + j + 1);
        }
    }

    m.indexCount = (int)idx.size();

    glGenVertexArrays(1, &m.VAO);
    glGenBuffers(1, &m.VBO);
    glGenBuffers(1, &m.EBO);

    glBindVertexArray(m.VAO);

    glBindBuffer(GL_ARRAY_BUFFER, m.VBO);
    glBufferData(GL_ARRAY_BUFFER, data.size() * sizeof(float), data.data(), GL_STATIC_DRAW);

    glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, m.EBO);
    glBufferData(GL_ELEMENT_ARRAY_BUFFER, idx.size() * sizeof(unsigned int), idx.data(), GL_STATIC_DRAW);

    glVertexAttribPointer(0, 3, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)0);
    glEnableVertexAttribArray(0);

    glVertexAttribPointer(1, 3, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)(3 * sizeof(float)));
    glEnableVertexAttribArray(1);

    glVertexAttribPointer(2, 2, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)(6 * sizeof(float)));
    glEnableVertexAttribArray(2);

    glBindVertexArray(0);
    return m;
}

int main()
{
    // glfw init
    glfwInit();
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);

#ifdef __APPLE__
    glfwWindowHint(GLFW_OPENGL_FORWARD_COMPAT, GL_TRUE);
#endif

    GLFWwindow* window = glfwCreateWindow(SCR_WIDTH, SCR_HEIGHT, "Kinetic Sphere Lights", NULL, NULL);
    if (window == NULL)
    {
        std::cout << "Failed to create GLFW window\n";
        glfwTerminate();
        return -1;
    }
    glfwMakeContextCurrent(window);
    glfwSetFramebufferSizeCallback(window, framebuffer_size_callback);
    glfwSetCursorPosCallback(window, mouse_callback);
    glfwSetScrollCallback(window, scroll_callback);
    glfwSetInputMode(window, GLFW_CURSOR, GLFW_CURSOR_DISABLED);

    if (!gladLoadGLLoader((GLADloadproc)glfwGetProcAddress))
    {
        std::cout << "Failed to initialize GLAD\n";
        return -1;
    }

    glEnable(GL_DEPTH_TEST);

    // shaders
    Shader lightingShader("6.multiple_lights.vs", "6.multiple_lights.fs");
    Shader lightSphereShader("6.light_cube.vs", "6.light_cube.fs");


    // build sphere mesh
    SphereMesh sphere = buildSphere(1.0f, 48, 48);

    // textures (keep your current ones for now)
    unsigned int diffuseMap  = loadTexture(FileSystem::getPath("resources/textures/earth.png").c_str());
    unsigned int specularMap = loadTexture(FileSystem::getPath("resources/textures/earth_specular.png").c_str());

    lightingShader.use();
    lightingShader.setInt("material.diffuse", 0);
    lightingShader.setInt("material.specular", 1);

    // initial point lights (will be animated every frame)
    glm::vec3 pointLightPositions[4] = {
        glm::vec3( 2.0f,  0.0f,  0.0f),
        glm::vec3(-2.0f,  0.0f,  0.0f),
        glm::vec3( 0.0f,  0.0f,  2.0f),
        glm::vec3( 0.0f, 1000.0f, 0.0f) // disabled
    };

    // glow colors for 3 orbiting lights
    glm::vec3 glowColors[3] = {
        glm::vec3(0.2f, 0.8f, 1.0f), // cyan
        glm::vec3(1.0f, 0.3f, 0.9f), // magenta
        glm::vec3(1.0f, 0.9f, 0.2f)  // yellow
    };

    // -------- star points data --------
    const int STAR_COUNT = 2000;
    std::vector<glm::vec3> starPos;
    starPos.reserve(STAR_COUNT);

    std::mt19937 rng(1337);
    std::uniform_real_distribution<float> unif(-1.0f, 1.0f);
    std::uniform_real_distribution<float> rad(35.0f, 70.0f);

    for (int i = 0; i < STAR_COUNT; i++) {
        glm::vec3 d(unif(rng), unif(rng), unif(rng));
        d = glm::normalize(d);
        float r = rad(rng);
        starPos.push_back(d * r);
    }

    unsigned int starVAO, starVBO;
    glGenVertexArrays(1, &starVAO);
    glGenBuffers(1, &starVBO);

    glBindVertexArray(starVAO);
    glBindBuffer(GL_ARRAY_BUFFER, starVBO);
    glBufferData(GL_ARRAY_BUFFER, starPos.size() * sizeof(glm::vec3), starPos.data(), GL_STATIC_DRAW);

    glVertexAttribPointer(0, 3, GL_FLOAT, GL_FALSE, sizeof(glm::vec3), (void*)0);
    glEnableVertexAttribArray(0);

    glBindVertexArray(0);


    while (!glfwWindowShouldClose(window))
    {
        float currentFrame = (float)glfwGetTime();
        deltaTime = currentFrame - lastFrame;
        lastFrame = currentFrame;

        processInput(window);

        glClearColor(0.06f, 0.06f, 0.08f, 1.0f);
        glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

        // ---------- animate point lights (3 orbiting spheres) ----------
        float t = currentFrame;
        glm::vec3 center(0.0f, 0.0f, 0.0f);
        float R = 2.6f;      // orbit radius
        float yAmp = 0.6f;   // vertical wobble
        float w = 0.8f;      // angular speed

        const float PI = 3.14159265359f;
        for (int i = 0; i < 3; i++) {
            float phase = i * (2.0f * PI / 3.0f);
            pointLightPositions[i] = center + glm::vec3(
                R * std::cos(w * t + phase),
                yAmp * std::sin(1.3f * t + phase),
                R * std::sin(w * t + phase)
            );
        }
        pointLightPositions[3] = glm::vec3(0.0f, 1000.0f, 0.0f); // disable 4th

        // ---------- view / projection ----------
        glm::mat4 projection = glm::perspective(glm::radians(camera.Zoom),
                                                (float)SCR_WIDTH / (float)SCR_HEIGHT,
                                                0.1f, 100.0f);
        glm::mat4 view = camera.GetViewMatrix();

        // ---------- lighting shader (for the big silver sphere) ----------
        lightingShader.use();
        lightingShader.setVec3("viewPos", camera.Position);
        lightingShader.setMat4("projection", projection);
        lightingShader.setMat4("view", view);

        // material: make it more “metallic”
        lightingShader.setFloat("material.shininess", 128.0f);

        // directional light (soft)
        lightingShader.setVec3("dirLight.direction", -0.2f, -1.0f, -0.3f);
        lightingShader.setVec3("dirLight.ambient",   0.02f, 0.02f, 0.02f);
        lightingShader.setVec3("dirLight.diffuse",   0.15f, 0.15f, 0.15f);
        lightingShader.setVec3("dirLight.specular",  0.25f, 0.25f, 0.25f);

        // point lights 0..2 (colored, bright)
        for (int i = 0; i < 3; i++) {
            std::string s = "pointLights[" + std::to_string(i) + "].";
            lightingShader.setVec3(s + "position", pointLightPositions[i]);
            lightingShader.setVec3(s + "ambient",  0.02f * glowColors[i]);
            lightingShader.setVec3(s + "diffuse",  1.6f  * glowColors[i]);
            lightingShader.setVec3(s + "specular", 2.2f  * glowColors[i]);
            lightingShader.setFloat(s + "constant", 1.0f);
            lightingShader.setFloat(s + "linear", 0.09f);
            lightingShader.setFloat(s + "quadratic", 0.032f);
        }

        // disable pointLights[3]
        lightingShader.setVec3("pointLights[3].position", pointLightPositions[3]);
        lightingShader.setVec3("pointLights[3].ambient",  0.0f, 0.0f, 0.0f);
        lightingShader.setVec3("pointLights[3].diffuse",  0.0f, 0.0f, 0.0f);
        lightingShader.setVec3("pointLights[3].specular", 0.0f, 0.0f, 0.0f);
        lightingShader.setFloat("pointLights[3].constant", 1.0f);
        lightingShader.setFloat("pointLights[3].linear", 0.09f);
        lightingShader.setFloat("pointLights[3].quadratic", 0.032f);

        // optional spotlight (camera flashlight) - keep but dim
        lightingShader.setVec3("spotLight.position", camera.Position);
        lightingShader.setVec3("spotLight.direction", camera.Front);
        lightingShader.setVec3("spotLight.ambient",  0.0f, 0.0f, 0.0f);
        lightingShader.setVec3("spotLight.diffuse",  0.7f, 0.7f, 0.7f);
        lightingShader.setVec3("spotLight.specular", 1.0f, 1.0f, 1.0f);
        lightingShader.setFloat("spotLight.constant", 1.0f);
        lightingShader.setFloat("spotLight.linear", 0.09f);
        lightingShader.setFloat("spotLight.quadratic", 0.032f);
        lightingShader.setFloat("spotLight.cutOff", glm::cos(glm::radians(12.5f)));
        lightingShader.setFloat("spotLight.outerCutOff", glm::cos(glm::radians(15.0f)));

        // bind textures for big sphere
        glActiveTexture(GL_TEXTURE0);
        glBindTexture(GL_TEXTURE_2D, diffuseMap);
        glActiveTexture(GL_TEXTURE1);
        glBindTexture(GL_TEXTURE_2D, specularMap);

        // draw big center sphere
        glBindVertexArray(sphere.VAO);
        glm::mat4 model = glm::mat4(1.0f);
        model = glm::scale(model, glm::vec3(2.0f)); // big
        lightingShader.setMat4("model", model);
        glDrawElements(GL_TRIANGLES, sphere.indexCount, GL_UNSIGNED_INT, 0);

        // ---------- draw the 3 glowing small spheres (visual for point lights) ----------
        lightSphereShader.use();
        lightSphereShader.setMat4("projection", projection);
        lightSphereShader.setMat4("view", view);

        glBindVertexArray(sphere.VAO);
        for (int i = 0; i < 3; i++) {
            glm::mat4 lm = glm::mat4(1.0f);
            lm = glm::translate(lm, pointLightPositions[i]);
            lm = glm::scale(lm, glm::vec3(0.22f));
            lightSphereShader.setMat4("model", lm);
            lightSphereShader.setVec3("lightColor", glowColors[i]); // IMPORTANT
            glDrawElements(GL_TRIANGLES, sphere.indexCount, GL_UNSIGNED_INT, 0);
        }
        // ---- draw stars (points) using lightCubeShader ----
        glDepthMask(GL_FALSE);       // ไม่เขียน depth (background)
        glDisable(GL_CULL_FACE);     // กันหายบางมุม (optional)

        lightSphereShader.use();
        lightSphereShader.setMat4("projection", projection);
        lightSphereShader.setMat4("view", view);

        // model = identity (ดาวอยู่ใน world space รอบๆ)
        glm::mat4 starModel = glm::mat4(1.0f);
        lightSphereShader.setMat4("model", starModel);
        lightSphereShader.setVec3("lightColor", glm::vec3(0.9f, 0.95f, 1.0f));

        glBindVertexArray(starVAO);
        glPointSize(2.0f);           // ขนาดดาว
        glDrawArrays(GL_POINTS, 0, STAR_COUNT);
        glBindVertexArray(0);

        glDepthMask(GL_TRUE);


        glfwSwapBuffers(window);
        glfwPollEvents();
    }

    // cleanup
    glDeleteVertexArrays(1, &sphere.VAO);
    glDeleteBuffers(1, &sphere.VBO);
    glDeleteBuffers(1, &sphere.EBO);
    glDeleteVertexArrays(1, &starVAO);
    glDeleteBuffers(1, &starVBO);


    glfwTerminate();
    return 0;
}

void processInput(GLFWwindow *window)
{
    if (glfwGetKey(window, GLFW_KEY_ESCAPE) == GLFW_PRESS)
        glfwSetWindowShouldClose(window, true);

    if (glfwGetKey(window, GLFW_KEY_W) == GLFW_PRESS)
        camera.ProcessKeyboard(FORWARD, deltaTime);
    if (glfwGetKey(window, GLFW_KEY_S) == GLFW_PRESS)
        camera.ProcessKeyboard(BACKWARD, deltaTime);
    if (glfwGetKey(window, GLFW_KEY_A) == GLFW_PRESS)
        camera.ProcessKeyboard(LEFT, deltaTime);
    if (glfwGetKey(window, GLFW_KEY_D) == GLFW_PRESS)
        camera.ProcessKeyboard(RIGHT, deltaTime);
}

void framebuffer_size_callback(GLFWwindow* window, int width, int height)
{
    glViewport(0, 0, width, height);
}

void mouse_callback(GLFWwindow* window, double xposIn, double yposIn)
{
    float xpos = (float)xposIn;
    float ypos = (float)yposIn;

    if (firstMouse)
    {
        lastX = xpos;
        lastY = ypos;
        firstMouse = false;
    }

    float xoffset = xpos - lastX;
    float yoffset = lastY - ypos;

    lastX = xpos;
    lastY = ypos;

    camera.ProcessMouseMovement(xoffset, yoffset);
}

void scroll_callback(GLFWwindow* window, double xoffset, double yoffset)
{
    camera.ProcessMouseScroll((float)yoffset);
}

unsigned int loadTexture(char const * path)
{
    unsigned int textureID;
    glGenTextures(1, &textureID);

    int width, height, nrComponents;
    unsigned char *data = stbi_load(path, &width, &height, &nrComponents, 0);
    if (data)
    {
        GLenum format = GL_RGB;
        if (nrComponents == 1) format = GL_RED;
        else if (nrComponents == 3) format = GL_RGB;
        else if (nrComponents == 4) format = GL_RGBA;

        glBindTexture(GL_TEXTURE_2D, textureID);
        glTexImage2D(GL_TEXTURE_2D, 0, format, width, height, 0, format, GL_UNSIGNED_BYTE, data);
        glGenerateMipmap(GL_TEXTURE_2D);

        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);

        stbi_image_free(data);
    }
    else
    {
        std::cout << "Texture failed to load at path: " << path << "\n";
        stbi_image_free(data);
    }

    return textureID;
}
