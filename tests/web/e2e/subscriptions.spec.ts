import {
  test,
  expect,
  Page,
} from "../../../src/web/node_modules/@playwright/test";
import { join } from "node:path";

const user = "10000000-0000-0000-0000-000000000001";
const creator = "20000000-0000-0000-0000-000000000002";
const second = "30000000-0000-0000-0000-000000000003";
const video = "40000000-0000-0000-0000-000000000004";
const base = "/api/engagement/subscriptions";
const item = (id: string) => ({
  userId: id,
  createdAtUtc: "2026-09-10T12:00:00Z",
  sourcePartition: 0,
  sourceOffset: "10",
});

async function setup(page: Page) {
  let outgoing = true,
    incoming = true,
    offset = 10;
  let owner: string | null = creator;
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path === "/api/auth/me")
      return route.fulfill({
        json: {
          user: { id: user, username: "sanket", email: "sanket@example.test" },
          expiresAtUtc: "2026-12-01T00:00:00Z",
        },
      });
    if (path === "/api/auth/csrf") return route.fulfill({ status: 204 });
    if (path === "/api/users")
      return route.fulfill({
        json: [
          { id: creator, username: "Maya Films" },
          { id: second, username: "Pixel Studio" },
          { id: user, username: "sanket" },
        ],
      });
    if (path === base + "/status")
      return route.fulfill({
        json: url.searchParams
          .getAll("creatorIds")
          .map((id) => ({
            creatorId: id,
            isActive: id === creator && outgoing,
            sourcePartition: 0,
            sourceOffset: String(offset),
          })),
      });
    if (request.method() === "GET" && path === base)
      return route.fulfill({
        json: {
          items: outgoing ? [item(creator), item(second)] : [item(second)],
          nextCursor: null,
        },
      });
    if (request.method() === "GET" && path === base + "/subscribers")
      return route.fulfill({
        json: { items: incoming ? [item(creator)] : [], nextCursor: null },
      });
    if (path.startsWith(base + "/")) {
      const isIncoming = path.includes("/subscribers/");
      const active = request.method() === "PUT";
      if (isIncoming) incoming = active;
      else outgoing = active;
      return route.fulfill({
        status: 202,
        json: {
          subscriberId: isIncoming ? creator : user,
          creatorId: isIncoming ? user : creator,
          isActive: active,
          cachePending: false,
          sourcePartition: 0,
          sourceOffset: String(++offset),
        },
      });
    }
    if (path === "/api/feed/videos")
      return route.fulfill({ json: { items: [], nextCursor: null } });
    if (path === "/api/feed/videos/" + video)
      return route.fulfill({
        json: {
          id: video,
          ownerId: owner,
          title: "A quiet afternoon in the city",
          description: "Scenes from a Sunday walk.",
          uploadedAtUtc: "2026-09-10T12:00:00Z",
          availableAtUtc: "2026-09-10T12:00:00Z",
          hashtags: [],
          renditions: [],
        },
      });
    if (path.endsWith("/reaction"))
      return route.fulfill({ json: { reaction: "none" } });
    if (path.endsWith("/comments"))
      return route.fulfill({
        json: { items: [], nextCursor: null, totalCount: 0 },
      });
    if (path.endsWith("/summaries"))
      return route.fulfill({
        json: [
          {
            videoId: video,
            likeCount: 5,
            dislikeCount: 0,
            viewCount: 120,
            commentCount: 0,
          },
        ],
      });
    return route.fulfill({ json: [] });
  });
  return {
    errors,
    setOwner: (id: string | null) => {
      owner = id;
    },
  };
}

test.describe("Subscription browser behavior with controlled API responses", () => {
  test("supports private lists, subscribe back, removal, mobile layout and shared watch state", async ({
    page,
  }) => {
    const { errors } = await setup(page);
    await page.goto("/subscriptions");
    await expect(
      page.getByRole("heading", { name: "Subscriptions", exact: true }),
    ).toBeVisible();
    const maya = page.getByRole("listitem").filter({ hasText: "Maya Films" });
    await expect(maya).toBeVisible();
    await page.screenshot({
      path: join(
        __dirname,
        "../../../artifacts/screenshots/subscriptions-desktop.png",
      ),
      fullPage: true,
    });
    await maya
      .getByRole("button", { name: "Unsubscribe", exact: true })
      .click();
    await expect(maya).toHaveCount(0);
    await page
      .getByRole("button", { name: "My subscribers", exact: true })
      .click();
    await expect(
      maya.getByRole("button", { name: "Subscribe back" }),
    ).toBeEnabled();
    await maya.getByRole("button", { name: "Subscribe back" }).click();
    await expect(
      maya.getByRole("button", { name: "Subscribed", exact: true }),
    ).toBeDisabled();
    await maya.getByRole("button", { name: "Remove subscriber" }).click();
    await expect(
      page.getByRole("heading", { name: "No subscribers yet" }),
    ).toBeVisible();
    await page
      .getByRole("button", { name: "My subscriptions", exact: true })
      .click();
    await expect(maya).toBeVisible(); // Reverse subscription survives incoming removal.
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({
      path: join(
        __dirname,
        "../../../artifacts/screenshots/subscriptions-mobile.png",
      ),
      fullPage: true,
    });
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    await page.goto("/watch/" + video);
    await expect(
      page.getByRole("button", { name: "Subscribed", exact: true }),
    ).toBeVisible();
    expect(errors).toEqual([]);
  });

  test("shows the optimistic animation until acknowledgement and restores a rejected change", async ({
    page,
  }) => {
    await setup(page);
    await page.goto("/watch/" + video);
    const subscribed = page.getByRole("button", {
      name: "Subscribed",
      exact: true,
    });
    await expect(subscribed).toBeEnabled();
    let release!: () => void;
    const pending = new Promise<void>((resolve) => {
      release = resolve;
    });
    await page.route("**" + base + "/" + creator, async (route) => {
      await pending;
      await route.fulfill({ status: 403, json: {} });
    });
    await subscribed.click();
    const optimistic = page.getByRole("button", {
      name: "Subscribe",
      exact: true,
    });
    await expect(optimistic).toBeDisabled();
    await expect(optimistic).toHaveClass(/subscription-pending/);
    await page.emulateMedia({ reducedMotion: "reduce" });
    expect(
      await optimistic.evaluate(
        (element) => getComputedStyle(element).animationName,
      ),
    ).toBe("none");
    release();
    await expect(subscribed).toBeEnabled();
    await expect(
      page.getByRole("status").filter({ hasText: "rejected" }),
    ).toBeVisible();
  });

  test("hides Subscribe for own videos and unknown creators", async ({
    page,
  }) => {
    const context = await setup(page);
    context.setOwner(user);
    await page.goto("/watch/" + video);
    await expect(
      page.getByRole("heading", { name: "A quiet afternoon in the city" }),
    ).toBeVisible();
    await expect(page.locator(".subscribe-button")).toHaveCount(0);
    context.setOwner(null);
    await page.reload();
    await expect(
      page.getByRole("heading", { name: "A quiet afternoon in the city" }),
    ).toBeVisible();
    await expect(page.locator(".subscribe-button")).toHaveCount(0);
    expect(context.errors).toEqual([]);
  });
});
